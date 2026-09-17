using CRDTApp.Messages;
using CRDTApp.Schedulers;
using Newtonsoft.Json;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkManagers.Composite
{

    internal class FloodingManager : NetworkManager 
    {
        class FloodMessage
        {
            public ReplicaID OriginalSender { get; set; }
            public ReplicaID IntendedTarget { get; set; }
            public uint NumberOfHops { get; set; }
            public Message Message { get; set; }

            public FloodMessage() { 
            
            }

            public FloodMessage(ReplicaID intendedTarget, ReplicaID originalSender, Message message, uint numberOfHops = NUM_HOPS)
            {
                OriginalSender = originalSender;
                IntendedTarget = intendedTarget;
                Message = message;
                NumberOfHops = numberOfHops;
            }

            public string ToJson()
            {
                return JsonConvert.SerializeObject(this);
            }

            public static FloodMessage? FromJson(string json)
            {
                return JsonConvert.DeserializeObject<FloodMessage>(json);
            }

        }

        ReplicaID replicaID;
        HashSet<ReplicaID> activeConnections = new();
        HashSet<ReplicaID> innactiveConnections = new();
        IScheduler scheduler;
        NetworkManager lowerLevelManager;

        const uint NUM_HOPS = 10;
        public FloodingManager() { 
        
        }


        public void OnLowerLayerMessageReceived(ReplicaID senderID, Message lowerLayerMessage)
        {
            activeConnections.Add(senderID);
            innactiveConnections.Remove(senderID); // ASSUMPTION: CONNECTIONS ARE "SIMMETRIC", i.e. if A can reach B, then B can reach A

            var floodMessage = FloodMessage.FromJson(lowerLayerMessage.Payload);

            if (floodMessage == null) {
                throw new Exception("UNEXPECTED MESSAGE FORMAT IN FLOODING LAYER!");
            }

            if (floodMessage.IntendedTarget == replicaID)
            {
                CallMessageReceived(floodMessage.OriginalSender, floodMessage.Message);
            }
            else {
                if (floodMessage.NumberOfHops > 0) {
                    floodMessage.NumberOfHops -= 1;
                    var newMessage = new Message(floodMessage.ToJson());
                    foreach (var connection in activeConnections) {
                        lowerLevelManager.SendMessage(connection, newMessage);
                    }
                }
            }
        }


        


        /*

        public override Task Run()
        {
            return base.Run();
        }*/

        public override async Task<bool> SendMessage(ReplicaID recipient, Message message) {


            var cons = new List<ReplicaID>(activeConnections);

            List<Task<bool>> tasks = new();
            
            foreach (var connection in cons) {
               
                var newMessage = new Message(new FloodMessage(recipient,replicaID, message).ToJson());
                tasks.Add(lowerLevelManager.SendMessage(connection,newMessage));
            }


            while (tasks.Count > 0)
            {
                var completed = await Task.WhenAny(tasks);
                tasks.Remove(completed);

                if (await completed)
                    return true;
            }

            return false; 


        }



    }
}
