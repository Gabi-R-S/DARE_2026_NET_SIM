using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp
{
    internal interface INetworkManager
    {
        public Task Run();

        public void AddOnMessageReceivedHandler(Action<ReplicaID, Message> handler);
        public void RemoveOnMessageReceivedHandler(Action<ReplicaID, Message> handler);
        
        public bool SendMessage(ReplicaID recipient, Message message);

        public void AddOnConnectionInterruptedHandler(Action<ReplicaID> handler);
        public void RemoveOnConnectionInterruptedHandler(Action<ReplicaID> handler);
    }
}
