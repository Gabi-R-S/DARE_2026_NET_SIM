using CRDTApp.Messages;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkLayers.Composite
{
    public class TrackActiveConnectionsLayer:NetworkLayer
    {

        ReplicaID replicaID;
        ActiveConnectionTracker tracker;
        bool trackSends;
        public TrackActiveConnectionsLayer(NetworkLayer layerBelow, ActiveConnectionTracker tracker, ReplicaID replicaID,bool trackSends=false) : base(layerBelow)
        {
            this.tracker = tracker;
            this.replicaID = replicaID;
            layerBelow.AddOnConnectionInterruptedHandler(OnInterruption);
            layerBelow.AddOnConnectionRegainedHandler(OnConnectionRegained);
            layerBelow.AddOnMessageReceivedHandler(OnLowerLevelMessageReceived);
            this.trackSends = trackSends;
        }

        void OnInterruption(ReplicaID replica) {

            if (!Running)
            {
                return;
            }
            if (tracker.SetInnactive(replica) && replica != replicaID) {
                CallConnectionInterrupted(replica);
            }
        }

        void OnConnectionRegained(ReplicaID replica) {

            if (!Running)
            {
                return;
            }
            if (tracker.SetActive(replica)&&replica!=replicaID) {
                CallConnectionRegained(replica);
            }
        }

        void OnLowerLevelMessageReceived(ReplicaID sender, Message message) {

            if (!Running)
            {
                return;
            }
            tracker.SetActive(sender);
            CallMessageReceived(sender,message);
        }

        public override async Task<bool> SendMessage(ReplicaID recipient, Message message)
        {

            if (!Running)
            {
                return false;
            }

            var res = await LayerBelow.SendMessage(recipient, message);

            if (trackSends)
            {
                if (res)
                {
                    if (tracker.SetActive(recipient))
                    {
                        CallConnectionRegained(recipient);
                    }
                }
                else
                {
                    if (tracker.SetInnactive(recipient))
                    {
                        CallConnectionInterrupted(recipient);
                    }
                }
            }
            return res;
        }
    }
}
