using CRDTs;
using Shared;
using Simulator;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp
{
    internal class SimulatedNetworkManager : INetworkManager
    {
        ReplicaID replicaID;
        NetworkSimulator networkSimulator;
        
        event Action<ReplicaID, Message?>? MessageReceived;
        event Action<ReplicaID>? ConnectionInterrupted;
        public SimulatedNetworkManager(NetworkSimulator simulator, ReplicaID id)
        {
            this.networkSimulator = simulator;
            this.replicaID = id;
        }

        SemaphoreSlim? endProgramSemaphore;
        public void Stop()
        {
            endProgramSemaphore?.Release();
        }
        
        void OnMessageReceived(NetworkMessage message) {
            MessageReceived?.Invoke(message.Recipient,message.Payload as Message);
        }
        void OnConnectionInterrupted(ReplicaID destination) { 
            ConnectionInterrupted?.Invoke(destination);
        }
        public async Task Run() {
            networkSimulator.AddConnectionInterruptedHandler(replicaID, OnConnectionInterrupted);
            networkSimulator.AddMessageDeliveredHandler(replicaID,OnMessageReceived);

            endProgramSemaphore = new SemaphoreSlim(0, 1);
            await endProgramSemaphore.WaitAsync();
        }

        public void AddOnMessageReceivedHandler(Action<ReplicaID,Message> handler)
        {
            MessageReceived += handler;
        }

        public void RemoveOnMessageReceivedHandler(Action<ReplicaID, Message> handler)
        {
            MessageReceived -= handler;
        }

        public bool SendMessage(ReplicaID recipient, Message message)
        {
            return networkSimulator.ScheduleMessageDelivery(new NetworkMessage(this.replicaID, recipient, message.Payload));
        }


        public void AddOnConnectionInterruptedHandler(Action<ReplicaID> handler) { 
            ConnectionInterrupted += handler;
        }
        public void RemoveOnConnectionInterruptedHandler(Action<ReplicaID> handler) { 
            ConnectionInterrupted -= handler;
        }

    }
}
