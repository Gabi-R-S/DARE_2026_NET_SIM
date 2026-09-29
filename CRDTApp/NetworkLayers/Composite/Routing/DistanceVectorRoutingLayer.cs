using CRDTApp.Messages;
using CRDTApp.Schedulers;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;
using static CRDTApp.NetworkLayers.Composite.HeartbeatLayer;
using static CRDTApp.NetworkLayers.NetworkLayer;

namespace CRDTApp.NetworkLayers.Composite.Routing
{
    // THIS DOES THE WHOLE 2-in-1 thing with managing connection tracking and managing 
    public class DistanceVectorRoutingLayer : NetworkLayer
    {
        public class DistanceVectorMessage : NetworkLayerMessage<DistanceVectorMessage>
        {
            public ReplicaID OriginalSender { get; set; }
            public ReplicaID IntendedTarget { get; set; }
            public bool IsProtocolMessage { get; set; }
            public Message? Message { get; set; }

            public DistanceVectorMessage()
            {

            }

            public DistanceVectorMessage(ReplicaID intendedTarget, ReplicaID originalSender, Message? message, bool isProtocolMessage)
            {
                OriginalSender = originalSender;
                IntendedTarget = intendedTarget;
                Message = message;
                IsProtocolMessage = isProtocolMessage;

            }
        }


        class DistanceTableUpdateMessage : NetworkLayerMessage<DistanceTableUpdateMessage>
        {
            public Dictionary<ReplicaID, float> Distances { get; set; } = new();
            public DateTime Timestamp { get; set; }

            public DistanceTableUpdateMessage(Dictionary<ReplicaID, float> distances, DateTime timestamp) {
                Distances = distances;
                Timestamp = timestamp;
            }

        }


        class DistanceTable
        {
            Dictionary<ReplicaID, (float, ReplicaID)> distances; // target -> (distance, next in sequence)

            public DistanceTable(ReplicaID id)
            {
                distances = new Dictionary<ReplicaID, (float, ReplicaID)>();
                distances[id] = (0,id);
            }

            public void ResetConnection(ReplicaID replica)
            {
                distances.Remove(replica);
            }

            public void Update(ReplicaID us, ReplicaID sender, DistanceTableUpdateMessage update, IScheduler scheduler)
            {
                var timeNow = scheduler.GetCurrentTime();
                var timeDifference = (float)(timeNow - update.Timestamp).TotalSeconds;
                foreach (var other in update.Distances)
                {
                    var distance = other.Value + timeDifference;
                    if (!distances.ContainsKey(other.Key))
                    {
                        distances[other.Key] = (distance, sender);
                    }
                    else
                    {

                        if (sender == distances[other.Key].Item2)
                        {
                            distances[other.Key] = (other.Value, other.Key);
                        }
                        else
                        {
                            var currentValue = distances[other.Key].Item1;
                            if (distance < currentValue)
                            {
                                distances[other.Key] = (distance, sender);

                            }
                            else if (distance == currentValue)
                            {
                                if (distances[other.Key].Item2 != other.Key) {
                                    distances[other.Key] = (distance, sender);
                                }
                            }
                        }
                    }

                }
            }

            public ReplicaID GetNextInPathTo(ReplicaID target)
            {
                if (distances.ContainsKey(target))
                {
                    return distances[target].Item2;
                }
                return target;
            }

            public DistanceTableUpdateMessage GetUpdate(IScheduler scheduler)
            {
                Dictionary<ReplicaID, float> messgDistances = new();
                var timestamp = scheduler.GetCurrentTime();

                foreach ((float k,ReplicaID v) in distances.Values) {
                    messgDistances[v]=k;
                }


                return new DistanceTableUpdateMessage(messgDistances,timestamp);
            }
        }

        ReplicaID replicaID;
        ActiveConnectionTracker connectionTracker;
        IScheduler scheduler;
        DistanceTable table;

        bool running = true;

        public DistanceVectorRoutingLayer(ReplicaID replicaID, ActiveConnectionTracker connectionTracker, NetworkLayer layerBelow, IScheduler scheduler) : base(layerBelow, false, true, true)
        {
            this.connectionTracker = connectionTracker;
            this.replicaID = replicaID;
            this.scheduler = scheduler;
            table = new DistanceTable(replicaID);
            layerBelow.AddOnMessageReceivedHandler(OnLowerLayerMessageReceived);
            AddOnConnectionInterruptedHandler(table.ResetConnection);
        }



        public void OnLowerLayerMessageReceived(ReplicaID senderID, Message lowerLayerMessage)
        {
            var message = DistanceVectorMessage.FromJson(lowerLayerMessage.Payload);
            if (message == null)
            {
                throw new Exception("WRONG MESSAGE FORMAT IN DISTANCE VECTOR ROUTING");

            }

            connectionTracker.SetActive(senderID);

            if (message.IsProtocolMessage)
            {
                var distanceTableUpdate = DistanceTableUpdateMessage.FromJson(message.Message.Payload);
                if (distanceTableUpdate == null)
                {
                    throw new Exception("WRONG MESSAGE FORMAT IN DISTANCE VECTOR ROUTING UPDATE MESSAGE");

                }
                table.Update(replicaID, message.OriginalSender, distanceTableUpdate, scheduler);
            }
            else
            {
                if (message.IntendedTarget == replicaID)
                {
                    CallMessageReceived(message.OriginalSender, message.Message);
                }
                else
                {
                    var target = table.GetNextInPathTo(message.IntendedTarget);
                    LayerBelow.SendMessage(target, lowerLayerMessage);
                }

            }

        }



        public override Task<bool> SendMessage(ReplicaID recipient, Message message)
        {

            var newMessage = new Message(new DistanceVectorMessage(recipient, replicaID, message, false).ToJson());

            var target = table.GetNextInPathTo(recipient);


            return LayerBelow?.SendMessage(target, newMessage);

        }


        float heartbeatTime = 1.0f;

        async Task Hearbeat(ReplicaID replica)
        {
            if (running)
            {
                DistanceTableUpdateMessage message = table.GetUpdate(scheduler);
                Message messg = new Message(message.ToJson());

                LayerBelow.SendMessage(replica, messg);
                scheduler.ExecuteAfter(heartbeatTime, () => { Hearbeat(replica).Wait(); });
            }
        }

        public override Task Run()
        {
            var t = base.Run();
            foreach (var replica in connectionTracker.AllConnections)
            {
                Hearbeat(replica);
            }
            return t;
        }
        
        public override void Stop()
        {
            running = false;

            LayerBelow?.Stop();
        }


    }
}
