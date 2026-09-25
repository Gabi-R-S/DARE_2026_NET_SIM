using CRDTApp.NetworkLayers;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;
using CRDTApp.Messages;

namespace Tests_1
{
    internal class MockNetworkManager:NetworkLayer
    {
        public Queue<(ReplicaID, Message)> MessagesSent { get; private set; } = new();
        public Func<ReplicaID, Message, Task<bool>>? OnSendMessageFunc { get; set; }

        public override async Task<bool> SendMessage(ReplicaID recipient, Message message) {
            MessagesSent.Enqueue((recipient, message));
            if (OnSendMessageFunc != null) {
                return await OnSendMessageFunc(recipient, message);
            }
            return true;
        }

        public void ReceiveMessage(ReplicaID sender, Message message) {
            CallMessageReceived(sender, message);
        }

        public void TriggerConnectionInterrupted(ReplicaID replica) {
            CallConnectionInterrupted(replica);
        }

        public void TriggerConnectionRegained(ReplicaID replica) {
            CallConnectionRegained(replica);
        }
    }
}
