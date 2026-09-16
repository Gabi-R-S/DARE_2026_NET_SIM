using CRDTApp.Messages;
using CRDTs;
using Shared;
using Simulator;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkManagers
{
    public class SimulatedNetworkManager : INetworkManager
    {
        ReplicaID replicaID;
        NetworkSimulator networkSimulator;
        
        event Action<ReplicaID, Message?>? MessageReceived;
        event Action<ReplicaID>? ConnectionInterrupted;
        public SimulatedNetworkManager(NetworkSimulator simulator, ReplicaID id)
        {

            this.networkSimulator = simulator;
            this.replicaID = id;

            networkSimulator.AddConnectionInterruptedHandler(replicaID, OnConnectionInterrupted);
            networkSimulator.AddMessageDeliveredHandler(replicaID, OnMessageReceived);

        }

        
        SemaphoreSlim? endProgramSemaphore;
        public void Stop()
        {
            endProgramSemaphore?.Release();
        }
        
        void OnMessageReceived(NetworkMessage message) {
            MessageReceived?.Invoke(message.Recipient,new Message(message.Payload));
        }
        void OnConnectionInterrupted(ReplicaID destination) { 
            ConnectionInterrupted?.Invoke(destination);
        }
        public async Task Run() {

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
        public void AddOnConnectionInterruptedHandler(Action<ReplicaID> handler)
        {
            ConnectionInterrupted += handler;
        }
        public void RemoveOnConnectionInterruptedHandler(Action<ReplicaID> handler)
        {
            ConnectionInterrupted -= handler;
        }
        public bool SendMessage(ReplicaID recipient, Message message)
        {
            return networkSimulator.ScheduleMessageDelivery(new NetworkMessage(this.replicaID, recipient, message.Payload));
        }


        

    }
}
