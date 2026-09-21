using CRDTApp.Messages;
using CRDTs;
using Shared;
using Simulator;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkLayers.Base
{
    public class SimulatedNetworkBaseLayer : NetworkLayer
    {
        ReplicaID replicaID;
        NetworkSimulator networkSimulator;
        
        public SimulatedNetworkBaseLayer(NetworkSimulator simulator, ReplicaID id)
        {

            this.networkSimulator = simulator;
            this.replicaID = id;

            networkSimulator.AddConnectionInterruptedHandler(replicaID, CallConnectionInterrupted);
            networkSimulator.AddMessageDeliveredHandler(replicaID, OnSimulatorMessageReceived);

        }

        void OnSimulatorMessageReceived(NetworkMessage message) {
            CallMessageReceived(message.Source, new Message(message.Payload));
        }

        
        SemaphoreSlim? endProgramSemaphore;
        public void Stop()
        {
            endProgramSemaphore?.Release();
        }
        
      
        public override async Task Run() {

            endProgramSemaphore = new SemaphoreSlim(0, 1);
            await endProgramSemaphore.WaitAsync();
        }

        
        public override async Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            return networkSimulator.ScheduleMessageDelivery(new NetworkMessage(this.replicaID, recipient, message.Payload));
        }


        

    }
}
