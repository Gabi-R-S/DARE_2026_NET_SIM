using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
using CRDTApp.NetworkLayers.Composite.Routing.Helper;
using CRDTApp.Schedulers;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;
using static CRDTApp.NetworkLayers.Composite.HeartbeatLayer;
using static CRDTApp.NetworkLayers.NetworkLayer;

namespace CRDTApp.NetworkLayers.Composite.Routing
{

    public class DistanceVectorRoutingLayer : NetworkLayer
    {
     




        ReplicaID replicaID;
        ActiveConnectionTracker connectionTracker;
        IScheduler scheduler;
        DistanceTable table;

        uint maxTTLHops;
        HashSet<ReplicaID> sendingTo;

        public DistanceVectorRoutingLayer(ReplicaID replicaID, ActiveConnectionTracker connectionTracker, NetworkLayer layerBelow, IScheduler scheduler,uint maxTTLHops) : base(layerBelow, false, true, true)
        {
            this.connectionTracker = connectionTracker;
            this.replicaID = replicaID;
            this.scheduler = scheduler;
            table = new DistanceTable(replicaID);
            this.maxTTLHops = maxTTLHops;
            layerBelow.AddOnMessageReceivedHandler(OnLowerLayerMessageReceived);
            AddOnConnectionInterruptedHandler(table.ResetConnection);
            AddOnConnectionRegainedHandler(OnConnectionGained);
        }

        void OnConnectionGained(ReplicaID replica)
        {
            if (replica != replicaID && !sendingTo.Contains(replica) && Running)
            {
                sendingTo.Add(replica);
                Hearbeat(replica);
            }
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
                table.Update(replicaID, senderID, distanceTableUpdate, scheduler);
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
                    message.TTLhops--;
                    LayerBelow?.SendMessage(target, message.AsMessage());
                }

            }

        }



        public override Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            var newMessage = new DistanceVectorMessage(recipient, replicaID, message, false,maxTTLHops).AsMessage();
            
            if (recipient == replicaID) {
                return LayerBelow?.SendMessage(recipient,newMessage);
            }
            
            
            var target = table.GetNextInPathTo(recipient);


            return LayerBelow?.SendMessage(target, newMessage);

        }


        float heartbeatTime = 1.0f;

        async Task Hearbeat(ReplicaID replica)
        {
            if (Running)
            {
                DistanceTableUpdateMessage message = table.GetUpdate(scheduler,replica);
                Message messg = new Message(message.ToJson());

                DistanceVectorMessage mes = new DistanceVectorMessage(replica,replicaID,messg,true,1);

                LayerBelow.SendMessage(replica, new Message(mes.ToJson()));

                scheduler.ExecuteAfter(heartbeatTime, () => {
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
            var t = base.Run();
            sendingTo = new HashSet<ReplicaID>();
            foreach (var replica in connectionTracker.AllConnections)
            {
                if (replica != replicaID)
                {
                    sendingTo.Add(replica);
                    Hearbeat(replica);
                }
            }
            return t;
        }



    }
}
