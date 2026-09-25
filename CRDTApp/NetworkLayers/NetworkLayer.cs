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
        public NetworkLayer? LayerBelow { get; private set; }
        public NetworkLayer(NetworkLayer? layerBelow = null, bool connectMessageReceived=false, bool connectConnectionInterrupted = false, bool connectConnectionRegained = false) {
            this.LayerBelow = layerBelow;

            if (layerBelow != null) {
                if (connectMessageReceived) {
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
        public abstract class NetworkLayerMessage<T> {

            public string ToJson() {

                return JsonConvert.SerializeObject(this);
            }
            static public T? FromJson(string json)
            {
                return JsonConvert.DeserializeObject<T>(json);
            }
        
        }
        public virtual Task Run(){
            if (LayerBelow == null) {
                return Task.CompletedTask;
            }
            return LayerBelow.Run();
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
