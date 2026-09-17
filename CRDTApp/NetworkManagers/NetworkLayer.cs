using CRDTApp.Messages;
using Shared;
using Simulator;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkManagers
{
    public abstract class NetworkLayer
    {
        public virtual Task Run(){
            return Task.CompletedTask;
        }

        object receiveLock = new object();
        object interruptedLock = new object();
        object regainedLock = new object();
        event Action<ReplicaID, Message?>? MessageReceived;
        event Action<ReplicaID>? ConnectionInterrupted;
        event Action<ReplicaID>? ConnectionRegained;

        protected void CallConnectionRegained(ReplicaID replica) {
            lock (regainedLock) {
                ConnectionRegained?.Invoke(replica);
            }
        }
        protected void CallMessageReceived(ReplicaID replica, Message? message)
        {
            lock (receiveLock)
            {
                MessageReceived?.Invoke(replica, message);
            }
        }

        protected  void CallConnectionInterrupted(ReplicaID replica)
        {
            lock (interruptedLock)
            {
                ConnectionInterrupted?.Invoke(replica);
            }
        }

        public void AddOnMessageReceivedHandler(Action<ReplicaID, Message> handler)
        {
            lock (receiveLock)
            {
                MessageReceived += handler;
            }
        }

        public void RemoveOnMessageReceivedHandler(Action<ReplicaID, Message> handler)
        {
            lock (receiveLock)
            {
                MessageReceived -= handler;
            }
        }

        public void AddOnConnectionInterruptedHandler(Action<ReplicaID> handler)
        {
            lock (interruptedLock)
            {
                ConnectionInterrupted += handler;
            }
        }
        public void RemoveOnConnectionInterruptedHandler(Action<ReplicaID> handler)
        {
            lock (interruptedLock)
            {
                ConnectionInterrupted -= handler;
            }
        }

        public void AddOnConnectionRegainedHandler(Action<ReplicaID> handler)
        {
            lock (regainedLock)
            {
                ConnectionRegained += handler;
            }
        }
        public void RemoveOnConnectionRegainedHandler(Action<ReplicaID> handler)
        {
            lock (regainedLock)
            {
                ConnectionRegained -= handler;
            }
        }
        public abstract Task<bool> SendMessage(ReplicaID recipient, Message message);

    }
}
