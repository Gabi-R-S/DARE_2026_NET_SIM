using Shared;
using SimSharp;
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
        Simulation env;

        public NetworkSimulator(Simulation env,ConnectionMap connectionMap) {
            this.env=env;
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



        public bool ScheduleMessageDelivery( NetworkMessage message)
        {
            if (connectionMap.IsConnectionAvailable(message.Source,message.Recipient)) {
                var connection = connectionMap.GetConnection(message.Source,message.Recipient);
                if (connection != null)
                {
                    Event ev = new Event(env);
                    ev.AddCallback( (env) => {
                        if (messageDeliveredHandlers.ContainsKey(message.Recipient))
                        {
                            foreach (var callback in messageDeliveredHandlers[message.Recipient])
                            {
                                callback(message);
                            }
                        }
                    });
                    env.ScheduleD(connection.TransmissionTime, ev);
                    return true;
                }
            }
            
            return false;

        }

        public void ExecuteAfter(float time, Action action)
        {
            var actionEvent = new Event(env);
            actionEvent.AddCallback(e => action());
            env.ScheduleD(time, actionEvent);
        }


    }
}
