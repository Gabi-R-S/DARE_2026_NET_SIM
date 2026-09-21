using CRDTApp.Messages;
using CRDTApp.Schedulers;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkLayers.Composite
{
    public class TrackMessageReceptionTimeoutLayer : NetworkLayer
    {
        IScheduler scheduler;
        ReplicaID replicaID;
        float timeoutTime;
        Dictionary<ReplicaID, DateTime> lastCommunication = new();
        public TrackMessageReceptionTimeoutLayer(ReplicaID replicaID, float timeoutTime, IScheduler scheduler,NetworkLayer layerBelow) : base(layerBelow, false, true, true)
        {
            this.scheduler = scheduler;
            this.replicaID = replicaID;
            this.timeoutTime = timeoutTime;

            layerBelow.AddOnMessageReceivedHandler(OnLowerLevelMessageReceived);
        }


        void SetTime(ReplicaID replica) {
            float timeSinceTimeout = -1;

            var currentTime = scheduler.GetCurrentTime();
            if (lastCommunication.ContainsKey(replica)) {
                var lastTime =lastCommunication[replica];

                timeSinceTimeout = (float)(currentTime - lastTime).TotalSeconds;
            }

            lastCommunication[replica] = currentTime;

            if (replica != replicaID)
            {
                if ((timeSinceTimeout < 0 || timeSinceTimeout > timeoutTime))
                {
                    CallConnectionRegained(replica);
                }
                scheduler.ExecuteAfter(timeoutTime, () =>
                {
                    if (lastCommunication[replica] == currentTime)
                    {
                        CallConnectionInterrupted(replica);
                    }
                });
            }
        }
        void OnLowerLevelMessageReceived(ReplicaID sender, Message message)
        {
            SetTime(sender);
            CallMessageReceived(sender, message);
        }
        public override async Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            var res = await layerBelow.SendMessage(recipient, message);

            if (res) {
                SetTime(recipient);
            }

            return res;
        }

      
    }
}
