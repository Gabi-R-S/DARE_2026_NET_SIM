using CRDTApp.Messages;
using Newtonsoft.Json;
using Shared;
using Simulator;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkLayers
{
    public abstract class NetworkLayer
    {
        public virtual void Stop()
        {
            Running = false;
            LayerBelow?.Stop();
        }
        public volatile bool Running;
        public NetworkLayer? LayerBelow { get; private set; }
        public NetworkLayer(NetworkLayer? layerBelow = null, bool connectMessageReceived = false, bool connectConnectionInterrupted = false, bool connectConnectionRegained = false)
        {
            this.LayerBelow = layerBelow;

            if (layerBelow != null)
            {
                if (connectMessageReceived)
                {
                    layerBelow.AddOnMessageReceivedHandler(CallMessageReceived);
                }
                if (connectConnectionInterrupted)
                {
                    layerBelow.AddOnConnectionInterruptedHandler(CallConnectionInterrupted);
                }
                if (connectConnectionRegained)
                {
                    layerBelow.AddOnConnectionRegainedHandler(CallConnectionRegained);
                }

            }

        }
        public virtual Task Run()
        {
            Running = true;
            if (LayerBelow == null)
            {
                return Task.CompletedTask;
            }
            return LayerBelow.Run();
        }


        event Action<ReplicaID, Message?>? MessageReceived;
        event Action<ReplicaID>? ConnectionInterrupted;
        event Action<ReplicaID>? ConnectionRegained;

        protected void CallConnectionRegained(ReplicaID replica)
        {
            ConnectionRegained?.Invoke(replica);
        }
        protected void CallMessageReceived(ReplicaID replica, Message? message)
        {
            MessageReceived?.Invoke(replica, message);
        }

        protected void CallConnectionInterrupted(ReplicaID replica)
        {
            ConnectionInterrupted?.Invoke(replica);
        }

        public void AddOnMessageReceivedHandler(Action<ReplicaID, Message> handler)
        {
            MessageReceived += handler;
        }

        public void RemoveOnMessageReceivedHandler(Action<ReplicaID, Message> handler)
        {
            MessageReceived -= handler;
        }

        public void AddOnConnectionInterruptedHandler(Action<ReplicaID> handler)
        {
            ConnectionInterrupted += handler;
        }
        public void RemoveOnConnectionInterruptedHandler(Action<ReplicaID> handler)
        {
                ConnectionInterrupted -= handler;
        }

        public void AddOnConnectionRegainedHandler(Action<ReplicaID> handler)
        {
                ConnectionRegained += handler;
        }
        public void RemoveOnConnectionRegainedHandler(Action<ReplicaID> handler)
        {
                ConnectionRegained -= handler;
        }
        public virtual Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            if (LayerBelow == null)
            {
                return Task.FromResult(false);
            }
            return LayerBelow.SendMessage(recipient, message);
        }

    }
}
