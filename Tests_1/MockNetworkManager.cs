using CRDTApp.NetworkManagers;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;
using CRDTApp.Messages;

namespace Tests_1
{
    internal class MockNetworkManager:NetworkManager
    {
        public Queue<(ReplicaID, Message)> MessagesSent { get; private set; } = new();


        public override Task<bool> SendMessage(ReplicaID recipient, Message message) {
            MessagesSent.Enqueue((recipient,message));
            return Task.FromResult(true);
        }



        public void ReceiveMessage(ReplicaID sender, Message message) {
            CallMessageReceived(sender,message);
        }
    }
}
