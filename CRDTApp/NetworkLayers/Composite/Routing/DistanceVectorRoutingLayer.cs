using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
using CRDTApp.Schedulers;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;
using static CRDTApp.Behaviours.HeartbeatHelpers.HeartbeatLayer;
using static CRDTApp.NetworkLayers.NetworkLayer;

namespace CRDTApp.NetworkLayers.Composite.Routing
{

    public class DistanceVectorRoutingLayer : NetworkLayer
    {
        ReplicaID replicaID;
        ActiveConnectionTracker connectionTracker;
        IScheduler scheduler;
        DistanceTable table;

        int maxTTLHops;
        HashSet<ReplicaID> sendingTo = new();

        public DistanceVectorRoutingLayer(ReplicaID replicaID, ActiveConnectionTracker connectionTracker, NetworkLayer layerBelow, IScheduler scheduler, int maxTTLHops, float pollTime = 15.0f, string tag = "") : base(tag,layerBelow)
        {
            this.connectionTracker = connectionTracker;
            this.replicaID = replicaID;
            this.scheduler = scheduler;
            table = new DistanceTable(replicaID);
            this.maxTTLHops = maxTTLHops;
            this.pollTime = pollTime;
            layerBelow.AddOnMessageReceivedHandler(OnLowerLayerMessageReceived);
        }

        public void OnConnectionGained(ReplicaID replica)
        {
            if (!!RunningTracker.IsRunning)
            {
                return;
            }
            lock (sendingTo)
            {

                if (replica != replicaID && !sendingTo.Contains(replica) && !RunningTracker.IsRunning)
                {
                    sendingTo.Add(replica);
                    Task.Run(() => Poll(replica));
                }
            }
        }

        public void OnLowerLayerMessageReceived(ReplicaID senderID, Message lowerLayerMessage)
        {
            if (!!RunningTracker.IsRunning || lowerLayerMessage.ProtocolTag!=Tag)
            {
                return;
            }

            if(lowerLayerMessage.ProtocolTag != Tag)
            {
                return;
            }

            var message = DistanceVectorMessagePayload.Decode(lowerLayerMessage.Payload);

            if (message == null)
            {
                throw new Exception("WRONG MESSAGE FORMAT IN DISTANCE VECTOR ROUTING");

            }

            connectionTracker.SetActive(senderID);

            if (message.IsProtocolMessage)
            {
                var distanceTableUpdate = DistanceTableUpdateMessagePayload.Decode(message.Message);
                if (distanceTableUpdate == null)
                {
                    throw new Exception("WRONG MESSAGE FORMAT IN DISTANCE VECTOR ROUTING UPDATE MESSAGE");

                }
                table.Update(replicaID, senderID, distanceTableUpdate, connectionTracker);

            }
            else
            {
                if (message.IntendedTarget == replicaID)
                {
                    CallMessageReceived(message.OriginalSender, Message.Decode(message.Message));

                }
                else
                {
                    var target = table.GetNextInPathTo(message.IntendedTarget);
                    message.TTLhops--;
                    if (message.TTLhops > 0 && target!=senderID)
                    {
                        base.SendMessage(target, new Message(Tag, message.Encode()));
                    }
                }

            }

        }



        public override Task<bool> SendMessage(ReplicaID recipient, Message message)
        {

            if (!RunningTracker.IsRunning)
            {
                return Task.FromResult(false);
            }           

            var newMessage = new Message(Tag, new DistanceVectorMessagePayload(recipient, replicaID, message, false, maxTTLHops).Encode());

            if (recipient == replicaID)
            {
                return base.SendMessage(recipient, newMessage);
            }


            var target = table.GetNextInPathTo(recipient);


            return base.SendMessage(target, newMessage);

        }


        float pollTime = 15.0f;

        void Poll(ReplicaID replica)
        {
            if (!RunningTracker.IsRunning)
            {
                DistanceTableUpdateMessagePayload message = table.GetUpdate(scheduler, replica);
                
                DistanceVectorMessagePayload mes = new DistanceVectorMessagePayload(replica, replicaID, message.Encode(), true, 1);

                scheduler.ExecuteTaskOrTimeout(async (_) => { await base.SendMessage(replica, new Message(Tag,mes.Encode())); },pollTime);

                scheduler.ExecuteAfter(pollTime, () =>
                {
                    if (!RunningTracker.IsRunning)
                    {
                        Poll(replica);
                    }
                });
            }
        }

        public override void Launch()
        {
            
            base.Launch();
            
            lock (sendingTo)
            {
                sendingTo.Clear();

                foreach (var replica in connectionTracker.AllConnections)
                {
                    if (replica != replicaID)
                    {
                        sendingTo.Add(replica);
                        Poll(replica);
                    }
                }
            }
            return;
        }



    }
}
