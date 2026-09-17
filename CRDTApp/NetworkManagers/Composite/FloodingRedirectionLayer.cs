using CRDTApp.Messages;
using CRDTApp.Schedulers;
using Newtonsoft.Json;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkManagers.Composite
{

    //IDEA: move the heartbeat mechanism to a different layer? doesn't work very well stacked on top of this one, though, I think...
    //TODO: make thread-safe
    internal class FloodingRedirectionLayer : NetworkLayer
    {
        class FloodMessage
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
        HashSet<ReplicaID> innactiveConnections;
        NetworkLayer lowerLevelManager;

        uint numHops = 10;

        public FloodingRedirectionLayer(ReplicaID replicaID, HashSet<ReplicaID> connections, NetworkLayer lowerLevelManager, uint numHops)
        {
            this.replicaID = replicaID;
            this.innactiveConnections = new HashSet<ReplicaID>(connections);
            this.lowerLevelManager = lowerLevelManager;

            lowerLevelManager.AddOnMessageReceivedHandler(OnLowerLayerMessageReceived);
            lowerLevelManager.AddOnConnectionInterruptedHandler(OnConnectionLost);
        }

        void OnConnectionLost(ReplicaID id)
        {
            innactiveConnections.Add(id);
            activeConnections.Add(id);
        }

        public void OnLowerLayerMessageReceived(ReplicaID senderID, Message lowerLayerMessage)
        {
            activeConnections.Add(senderID);
            innactiveConnections.Remove(senderID); // ASSUMPTION: CONNECTIONS ARE "SIMMETRIC", i.e. if A can reach B, then B can reach A

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
                    foreach (var connection in activeConnections)
                    {
                        if (connection != senderID)
                        {
                            lowerLevelManager.SendMessage(connection, newMessage);
                        }
                    }
                }
            }
        }




        async Task ProcessFurtherTasks(Dictionary<Task<bool>, ReplicaID> tasks)
        {

            while (tasks.Count > 0)
            {
                var completed = await Task.WhenAny(tasks.Keys);
                var replica = tasks[completed];
                tasks.Remove(completed);

                if (!await completed)
                {

                    activeConnections.Remove(replica);
                    innactiveConnections.Add(replica);
                }
            }
        }

        public override async Task<bool> SendMessage(ReplicaID recipient, Message message)
        {


            var cons = new List<ReplicaID>(activeConnections);

            Dictionary<Task<bool>, ReplicaID> tasks = new();

            foreach (var connection in cons)
            {

                var newMessage = new Message(new FloodMessage(recipient, replicaID, message, numHops).ToJson());
                tasks[(lowerLevelManager.SendMessage(connection, newMessage))] = connection;
            }


            while (tasks.Count > 0)
            {
                var completed = await Task.WhenAny(tasks.Keys);
                var replica = tasks[completed];
                tasks.Remove(completed);

                if (await completed)
                {
                    ProcessFurtherTasks(tasks);
                    return true;
                }
                else
                {
                    activeConnections.Remove(replica);
                    innactiveConnections.Add(replica);
                }
            }

            return false;


        }



    }
}
