using CRDTApp.Messages;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkLayers.Composite
{
    public class TrackActiveConnectionsLayer:NetworkLayer
    {

        ActiveConnectionTracker tracker;
        public TrackActiveConnectionsLayer(NetworkLayer layerBelow, ActiveConnectionTracker tracker) : base(layerBelow)
        {
            this.tracker = tracker;
            layerBelow.AddOnConnectionInterruptedHandler(OnInterruption);
            layerBelow.AddOnConnectionRegainedHandler(OnConnectionRegained);
            layerBelow.AddOnMessageReceivedHandler(OnLowerLevelMessageReceived);
        }

        void OnInterruption(ReplicaID replica) {
            if (tracker.SetInnactive(replica)) {
                CallConnectionInterrupted(replica);
            }
        }

        void OnConnectionRegained(ReplicaID replica) {
            if (tracker.SetActive(replica)) {
                CallConnectionRegained(replica);
            }
        }

        void OnLowerLevelMessageReceived(ReplicaID sender, Message message) {
            tracker.SetActive(sender);
            CallMessageReceived(sender,message);
        }

        public override async Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            var res = await layerBelow.SendMessage(recipient, message);
            if (res)
            {
                if (tracker.SetActive(recipient)) {
                    CallConnectionRegained(recipient);
                }
            }
            else
            {
                if (tracker.SetInnactive(recipient)) {
                    CallConnectionRegained(recipient);
                }
            }
            return res;
        }
    }
}
