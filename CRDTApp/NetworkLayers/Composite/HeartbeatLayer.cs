using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
using CRDTApp.Schedulers;
using Newtonsoft.Json;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkLayers.Composite
{
    public class HeartbeatLayer : NetworkLayer
    {
        float heartbeatTime = 0.5f;
        IScheduler scheduler;
        ActiveConnectionTracker connectionTracker;

        HashSet<ReplicaID> sendingTo = new HashSet<ReplicaID>();
        ReplicaID replicaID;

        public HeartbeatLayer(ActiveConnectionTracker connectionTracker, NetworkLayer layerBelow, IScheduler scheduler, float heartbeatTime,ReplicaID replicaID) : base(layerBelow, false, true, true)
        {
            this.connectionTracker = connectionTracker;
            this.scheduler = scheduler;
            this.heartbeatTime = heartbeatTime;
            this.replicaID = replicaID;
            layerBelow.AddOnMessageReceivedHandler(OnLowerLevelMessageReceived);
            AddOnConnectionRegainedHandler(OnConnectionGained);
        }

        void OnConnectionGained(ReplicaID replica) {
            lock (sendingTo)
            {
                if (replica != replicaID && !sendingTo.Contains(replica) && Running)
                {
                    sendingTo.Add(replica);
                    Hearbeat(replica);
                }
            }
        }

        void OnLowerLevelMessageReceived(ReplicaID replica, Message message)
        {
            var heartbeatMessage = HeartbeatMessage.FromJson(message.Payload);
            if (heartbeatMessage == null)
            {
                throw new Exception("Unexpected message format in heartbeat layer");
            }

            if (!heartbeatMessage.HeartBeat)
            {
                CallMessageReceived(replica, heartbeatMessage.Message);
            }
        }


        async Task Hearbeat(ReplicaID replica)
        {
            if (Running)
            {
                HeartbeatMessage message = new HeartbeatMessage(null, true);
                Message messg = new Message(message.ToJson());

                LayerBelow.SendMessage(replica, messg);

                scheduler.ExecuteAfter(heartbeatTime, () =>
                {
                    if (Running)
                    {
                        Hearbeat(replica);
                    }
                });
            }
        }

        public override Task Run()
        {
            Running = true;
            var t = LayerBelow.Run();
            lock (sendingTo)
            {
                sendingTo.Clear();
            foreach (var replica in connectionTracker.AllConnections)
            {
                if (replica != replicaID)
                {
                    sendingTo.Add(replica);
                    Hearbeat(replica);
                }
            }
            }
            return t;
        }


        public override Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            return LayerBelow.SendMessage(recipient, new Message(new HeartbeatMessage(message).ToJson()));
        }



    }
}
