using CRDTApp.Messages;
using Newtonsoft.Json;
using Shared;
using Simulator;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace CRDTApp.NetworkLayers
{
    public abstract class NetworkLayer
    {
        public string Tag { get; private set; }
        protected IsRunningTracker RunningTracker { get; private set; }

        public NetworkLayer? LayerBelow { get; private set; }
        public NetworkLayer(string tag,IsRunningTracker runningTracker, NetworkLayer? layerBelow = null, bool connectMessageReceived = false)
        {
            Tag = tag;
            this.LayerBelow = layerBelow;
            this.RunningTracker = runningTracker;

            if (layerBelow != null)
            {
                if (connectMessageReceived)
                {
                    layerBelow.AddOnMessageReceivedHandler(CallMessageReceived);
                }
            }

        }
        public virtual void Launch()
        {
            if (LayerBelow == null)
            {
                return;
            }
            LayerBelow.Launch();
        }

        event Action<ReplicaID, Message?>? MessageReceived;
        protected void CallMessageReceived(ReplicaID replica, Message? message)
        {
            if (!RunningTracker.IsRunning)
            {
                return;
            }
            MessageReceived?.Invoke(replica, message);
        }
        public void AddOnMessageReceivedHandler(Action<ReplicaID, Message> handler)
        {
            MessageReceived += handler;
        }

        public void RemoveOnMessageReceivedHandler(Action<ReplicaID, Message> handler)
        {
            MessageReceived -= handler;
        }

        public virtual Task<bool> SendMessage(ReplicaID recipient, Message message)
        {

            if (!RunningTracker.IsRunning)
            {
                return Task.FromResult(false);
            }

            if (LayerBelow == null)
            {
                return Task.FromResult(false);
            }
            return LayerBelow.SendMessage(recipient, message);
        }

    }
}
