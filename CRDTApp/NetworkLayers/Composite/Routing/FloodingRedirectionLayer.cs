using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
using CRDTApp.Schedulers;
using Newtonsoft.Json;
using Shared;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace CRDTApp.NetworkLayers.Composite.Routing
{

    //TODO: make thread-safe
    public class FloodingRedirectionLayer : NetworkLayer
    {

        ReplicaID replicaID;
        ActiveConnectionTracker connectionTracker;

        int numHops = 10;


        object tagLock = new object();
        uint tag = 0;

        uint GetNextTag()
        {
            lock (tagLock)
            {
                var t = tag;
                tag++;
                return t;
            }
        }

        IScheduler scheduler;
        HashSet<(uint, ReplicaID)> rememberedMessages = new();
        float forgettingTime = 60.0f;

        public FloodingRedirectionLayer(ReplicaID replicaID, IScheduler scheduler, ActiveConnectionTracker connectionTracker, NetworkLayer layerBelow, int numHops, float forgettingTime) : base(layerBelow, false, true, true)
        {
            this.scheduler = scheduler;
            this.connectionTracker = connectionTracker;
            this.replicaID = replicaID;
            this.numHops = numHops;
            this.forgettingTime = forgettingTime;
            layerBelow.AddOnMessageReceivedHandler(OnLowerLayerMessageReceived);

        }


        void Remember(ReplicaID senderID, uint tag)
        {
            lock (rememberedMessages)
            {
                rememberedMessages.Add((tag, senderID));
            }
            scheduler.ExecuteAfter(forgettingTime, () =>
            {
                lock (rememberedMessages)
                {
                    rememberedMessages.Remove((tag, senderID));
                }
            });
        }


        public void OnLowerLayerMessageReceived(ReplicaID senderID, Message lowerLayerMessage)
        {
            if (!Running)
            {
                return;
            }

            var floodMessage = FloodMessage.FromJson(lowerLayerMessage.Payload);

            if (floodMessage == null)
            {
                throw new Exception("UNEXPECTED MESSAGE FORMAT IN FLOODING LAYER!");
            }


            if (floodMessage.IntendedTarget == replicaID)
            {
                Remember(floodMessage.OriginalSender, floodMessage.Tag);
                CallMessageReceived(floodMessage.OriginalSender, floodMessage.Message);
            }
            else
            {
                bool toForward = false;
                lock (rememberedMessages)
                {
                    toForward = floodMessage.NumberOfHops > 0 && Running && floodMessage.OriginalSender != replicaID && !rememberedMessages.Contains((floodMessage.Tag, floodMessage.OriginalSender));
                }
                if (toForward)
                {
                    Remember(floodMessage.OriginalSender, floodMessage.Tag);
                    if (floodMessage.NumberOfHops > 0)
                    {
                        floodMessage.NumberOfHops -= 1;
                        var newMessage = floodMessage.AsMessage();
                        foreach (var connection in connectionTracker.ActiveConnections)
                        {
                            if (connection != senderID && connection != replicaID)
                            {
                                LayerBelow.SendMessage(connection, newMessage);
                            }
                        }
                    }
                }
            }



        }






        public override async Task<bool> SendMessage(ReplicaID recipient, Message message)
        {

            if (!Running)
            {
                return false;
            }

            var newMessage = new Message(new FloodMessage(recipient, replicaID, GetNextTag(), message, numHops).ToJson());

            if (recipient == replicaID)
            {



                return await LayerBelow.SendMessage(recipient, newMessage);
            }



            Dictionary<Task<bool>, ReplicaID> tasks = new();
            var activeConnections = connectionTracker.ActiveConnections;
            if ((activeConnections.Count > 0 && !activeConnections.Contains(replicaID)) || (activeConnections.Count > 1 && activeConnections.Contains(replicaID)))
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
