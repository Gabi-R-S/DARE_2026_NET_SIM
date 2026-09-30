using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
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
                if (floodMessage.NumberOfHops > 0 && Running&&floodMessage.OriginalSender!=replicaID)
                {
                    floodMessage.NumberOfHops -= 1;
                    var newMessage = new Message(floodMessage.ToJson());
                    foreach (var connection in connectionTracker.ActiveConnections)
                    {
                        if (connection != senderID&&connection!=replicaID)
                        {
                            LayerBelow.SendMessage(connection, newMessage);
                        }
                    }
                }
            }
        }




        

        public override async Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            var newMessage = new Message(new FloodMessage(recipient, replicaID, message, numHops).ToJson());

            if (recipient == replicaID) {



                return await LayerBelow.SendMessage(recipient,newMessage);
            }

            

            Dictionary<Task<bool>, ReplicaID> tasks = new();
            var activeConnections = connectionTracker.ActiveConnections;
            if ((activeConnections.Count>0&& !activeConnections.Contains(replicaID))|| (activeConnections.Count > 1 && activeConnections.Contains(replicaID)))
            {

                foreach (var connection in connectionTracker.ActiveConnections)
                {
                    if (connection != replicaID)
                    {
                        tasks[(LayerBelow.SendMessage(connection, newMessage))] = connection;
                    }
                }
            }
            else
            {
                tasks[(LayerBelow.SendMessage(recipient, newMessage))] = recipient;

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
