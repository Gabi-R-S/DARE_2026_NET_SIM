using CRDTApp.Messages;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkManagers.Composite
{
    internal class TrackActiveConnactionsLayer:NetworkLayer
    {

        ActiveConnectionTracker tracker;
        public TrackActiveConnactionsLayer(NetworkLayer layerBelow,ActiveConnectionTracker tracker) : base(layerBelow)   
        {
            this.tracker = tracker;
            layerBelow.AddOnConnectionInterruptedHandler(tracker.SetInnactive);
            layerBelow.AddOnConnectionInterruptedHandler(tracker.SetActive);
            layerBelow.AddOnMessageReceivedHandler(OnLowerLevelMessageReceived);
        }

        void OnLowerLevelMessageReceived(ReplicaID sender, Message message) {
            tracker.SetActive(sender);
            CallMessageReceived(sender,message);
        }
        public override async Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            if (await layerBelow.SendMessage(recipient, message)) {

                tracker.SetActive(recipient);
                return true;
            }
            tracker.SetInnactive(recipient);

            return false;
        }
    }
}
