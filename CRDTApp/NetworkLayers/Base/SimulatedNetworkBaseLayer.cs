using CRDTApp.Messages;
using CRDTs;
using Shared;
using Simulator;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace CRDTApp.NetworkLayers.Base
{
    public class SimulatedNetworkBaseLayer : NetworkLayer
    {
        ReplicaID replicaID;
        NetworkSimulator networkSimulator;

        public SimulatedNetworkBaseLayer(NetworkSimulator simulator, ReplicaID id):base("")
        {

            this.networkSimulator = simulator;
            this.replicaID = id;

            networkSimulator.AddMessageDeliveredHandler(replicaID, OnSimulatorMessageReceived);

        }

        void OnSimulatorMessageReceived(NetworkMessage message) {

            if (RunningTracker.IsRunning == false)
            {
                return;
            }
            CallMessageReceived(message.Source, Message.Decode(message.Payload));
        }


        
        public override async Task<bool> SendMessage(ReplicaID recipient, Message message)
        {

            if (RunningTracker.IsRunning == false)
            {
                return false;
            }

            return networkSimulator.ScheduleMessageDelivery(new NetworkMessage(this.replicaID, recipient, message.Payload));
        }


    }
}
