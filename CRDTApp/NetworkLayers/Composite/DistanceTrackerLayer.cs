using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
using CRDTApp.Schedulers;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkLayers.Composite
{
    public class DistanceTrackerLayer : NetworkLayer
    {
        ActiveConnectionTracker tracker;
        ReplicaID replicaID;
        IScheduler scheduler;
        public DistanceTrackerLayer(NetworkLayer lowerLayer, ActiveConnectionTracker tracker, ReplicaID replicaID, IScheduler scheduler) : base(lowerLayer,false,false,true)
        {
            this.tracker = tracker;
            this.replicaID = replicaID;
            this.scheduler = scheduler;
            lowerLayer.AddOnConnectionInterruptedHandler(OnConnectionInterrupted);
            lowerLayer.AddOnMessageReceivedHandler(OnLowerLayerMessageReceived);
        }
       
        public void OnConnectionInterrupted(ReplicaID replica)
        {

            if (!IsRunning)
            {
                return;
            }

            if (replica != replicaID) {
                tracker.SetDistance(replica, float.PositiveInfinity);
            }
            CallConnectionInterrupted(replica);
        }
        public void OnLowerLayerMessageReceived(ReplicaID senderID, Message lowerLayerMessage)
        {

            if (!IsRunning)
            {
                return;
            }
            var message = DistanceTrackerMessage.Decode(lowerLayerMessage.Payload);
            if (message == null)
            {
                throw new Exception("Unexpected message format in distance tracker layer");
            }

            var currentTime = scheduler.GetCurrentTime();
            var timeDifference = (currentTime - message.Timestamp).TotalSeconds;

            tracker.SetDistance(senderID, (float)Math.Max(timeDifference, 0));
            CallMessageReceived(senderID, message.Message);
        }


        public override Task<bool> SendMessage(ReplicaID recipient, Message message)
        {

            if (!IsRunning)
            {
                return Task.FromResult(false);
            }

            var newMessage = new DistanceTrackerMessage(scheduler.GetCurrentTime(), message).AsMessage();

            return base.SendMessage(recipient, newMessage);

        }
    }
}
