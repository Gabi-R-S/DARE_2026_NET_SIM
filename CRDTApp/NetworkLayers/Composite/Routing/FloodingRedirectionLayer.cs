using CRDTApp.Messages;
using CRDTApp.Schedulers;
using Newtonsoft.Json;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkLayers.Composite.Routing
{

    //TODO: make thread-safe
    public class FloodingRedirectionLayer : NetworkLayer
    {
        public class FloodMessage : NetworkLayerMessage<FloodMessage>
        {
            public ReplicaID OriginalSender { get; set; }
            public ReplicaID IntendedTarget { get; set; }
            public uint NumberOfHops { get; set; }
            public Message? Message { get; set; }

            public FloodMessage()
            {

            }

            public FloodMessage(ReplicaID intendedTarget, ReplicaID originalSender, Message? message, uint numberOfHops)
            {
                OriginalSender = originalSender;
                IntendedTarget = intendedTarget;
                Message = message;
                NumberOfHops = numberOfHops;

            }

            
        }

        ReplicaID replicaID;
        ActiveConnectionTracker connectionTracker;
        
        uint numHops = 10;

        public FloodingRedirectionLayer(ReplicaID replicaID, ActiveConnectionTracker connectionTracker, NetworkLayer layerBelow, uint numHops) : base(layerBelow,false,true,true)
        {
            this.connectionTracker = connectionTracker;
            this.replicaID = replicaID;
            this.numHops = numHops;
            layerBelow.AddOnMessageReceivedHandler(OnLowerLayerMessageReceived);
            
        }

       

        public void OnLowerLayerMessageReceived(ReplicaID senderID, Message lowerLayerMessage)
        {
            var floodMessage = FloodMessage.FromJson(lowerLayerMessage.Payload);

            if (floodMessage == null)
            {
                throw new Exception("UNEXPECTED MESSAGE FORMAT IN FLOODING LAYER!");
            }


            if (floodMessage.IntendedTarget == replicaID)
            {
                CallMessageReceived(floodMessage.OriginalSender, floodMessage.Message);
            }
            else
            {
                if (floodMessage.NumberOfHops > 0)
                {
                    floodMessage.NumberOfHops -= 1;
                    var newMessage = new Message(floodMessage.ToJson());
                    foreach (var connection in connectionTracker.ActiveConnections)
                    {
                        if (connection != senderID)
                        {
                            layerBelow.SendMessage(connection, newMessage);
                        }
                    }
                }
            }
        }




        

        public override async Task<bool> SendMessage(ReplicaID recipient, Message message)
        {


            

            Dictionary<Task<bool>, ReplicaID> tasks = new();

            foreach (var connection in connectionTracker.ActiveConnections)
            {

                var newMessage = new Message(new FloodMessage(recipient, replicaID, message, numHops).ToJson());
                tasks[(layerBelow.SendMessage(connection, newMessage))] = connection;
            }


            while (tasks.Count > 0)
            {
                var completed = await Task.WhenAny(tasks.Keys);
                var replica = tasks[completed];
                tasks.Remove(completed);

                if (await completed)
                {
                    return true;
                }
            }

            return false;


        }



    }
}
