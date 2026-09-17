using CRDTApp.Messages;
using CRDTApp.Schedulers;
using Newtonsoft.Json;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkManagers.Composite
{
    internal class HeartbeatLayer:NetworkLayer
    {
        class HeartbeatMessage :NetworkLayerMessage<HeartbeatMessage>
        {
            public bool HeartBeat { get; set; }
            public Message? Message { get; set; }

            public HeartbeatMessage()
            {

            }

            public HeartbeatMessage(Message? message, bool heartbeat = false)
            {
                Message = message;

                HeartBeat = heartbeat;
            }

        }
        float heartbeatTime = 0.5f;
        IScheduler scheduler;
        ActiveConnectionTracker connectionTracker;

        public HeartbeatLayer(ActiveConnectionTracker connectionTracker, NetworkLayer layerBelow,IScheduler scheduler, float heartbeatTime) : base(layerBelow,false,true,true) {
            this.connectionTracker = connectionTracker;
            this.scheduler = scheduler;
            this.heartbeatTime = heartbeatTime;

            layerBelow.AddOnMessageReceivedHandler(OnLowerLevelMessageReceived);
        }


        void OnLowerLevelMessageReceived(ReplicaID replica, Message message){
            var heartbeatMessage = HeartbeatMessage.FromJson(message.Payload);
            if (heartbeatMessage == null) {
                throw new Exception("Unexpected message format in heartbeat layer");
            
            }

            if (!heartbeatMessage.HeartBeat) {
                CallMessageReceived(replica, heartbeatMessage.Message);
            }
        }

         
        async Task Hearbeat(ReplicaID replica)
        {
            HeartbeatMessage message = new HeartbeatMessage(null, true);
            Message messg = new Message(message.ToJson());

            if (!await layerBelow.SendMessage(replica, messg)) {
                CallConnectionInterrupted(replica);
            }
            

            scheduler.ExecuteAfter(heartbeatTime, () => { Hearbeat(replica); });
        }

        public override Task Run()
        {
            foreach (var replica in connectionTracker.AllConnections) {
                Hearbeat(replica); 
            }
            return base.Run();
        }


        public override Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            return layerBelow.SendMessage(recipient,new Message(new HeartbeatMessage(message).ToJson()));
        }

    }
}
