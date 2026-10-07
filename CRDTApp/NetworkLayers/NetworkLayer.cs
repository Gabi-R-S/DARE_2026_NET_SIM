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
        enum Running { 
        RUNNING,
        STOPPED
        }
        public virtual void Stop()
        {
            SetRunning(false);
            LayerBelow?.Stop();
            
        }
        volatile Running running = Running.STOPPED;

        protected void SetRunning(bool value)
        {
            Running previousValue = running;
            Debug.WriteLine(
                $"Network layer Running transition: {GetType().FullName} " +
                $"instance={RuntimeHelpers.GetHashCode(this)} {previousValue} -> {value}");
            running = value ? Running.RUNNING : Running.STOPPED;
        }
        public bool IsRunning { get { return running==Running.RUNNING; } }

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
            SetRunning(true);
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
            if (IsRunning == false) {
                return;
            }
            ConnectionRegained?.Invoke(replica);
        }
        protected void CallMessageReceived(ReplicaID replica, Message? message)
        {
            if (IsRunning == false)
            {
                return;
            }
            MessageReceived?.Invoke(replica, message);
        }

        protected void CallConnectionInterrupted(ReplicaID replica)
        {
            if (IsRunning == false)
            {
                return;
            }
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

            if (IsRunning == false)
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
