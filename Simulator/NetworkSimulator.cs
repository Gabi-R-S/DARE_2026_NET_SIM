using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace Simulator
{
    public class NetworkSimulator
    {
        Dictionary<ReplicaID, List<Action<NetworkMessage>>> messageDeliveredHandlers = new Dictionary<ReplicaID, List<Action<NetworkMessage>>>();

        Dictionary<ReplicaID, List<Action<ReplicaID>>> connectionInterruptedHandlers = new Dictionary<ReplicaID, List<Action<ReplicaID>>>();

        ConnectionMap connectionMap;

        public NetworkSimulator(ConnectionMap connectionMap) {
            this.connectionMap = connectionMap;
        }


        public void AddConnectionInterruptedHandler(ReplicaID source, Action<ReplicaID> handler) {
            if (!connectionInterruptedHandlers.ContainsKey(source)) {
                connectionInterruptedHandlers[source] = new List<Action<ReplicaID>>();
            } 
            connectionInterruptedHandlers[source].Add(handler);
        }

        public void RemoveConnectionInterruptedHandler(ReplicaID source, Action<ReplicaID> handler)
        {
            connectionInterruptedHandlers[source].Remove(handler);
        }

        public void AddMessageDeliveredHandler(ReplicaID id, Action<NetworkMessage> handler)
        {
            if (!messageDeliveredHandlers.ContainsKey(id)) {
                messageDeliveredHandlers[id] = new List<Action<NetworkMessage>>();
            }

            messageDeliveredHandlers[id].Add(handler);
        }

        public void RemoveMessageDeliveredHandler(ReplicaID id, Action<NetworkMessage> handler)
        {
            messageDeliveredHandlers[id].Remove(handler);
        }

        public void DeliverMessage(NetworkMessage message)
        {
            if (messageDeliveredHandlers.TryGetValue(message.Recipient, out var handlers))
            {
                foreach (var handler in handlers)
                {
                    handler(message);
                }   
            }
        }

        public bool ScheduleMessageDelivery(NetworkMessage message)
        {
            
            throw new NotImplementedException("Message delivery scheduling is not implemented yet.");
            // Implementation for scheduling message delivery
            //return Task.FromResult(true);
        }
    }
}
