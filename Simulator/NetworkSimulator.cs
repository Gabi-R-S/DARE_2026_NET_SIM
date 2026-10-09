using Shared;
using SimSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Simulator
{
    public abstract class NetworkSimulator
    {

        event Action RunEvents; //close events!
        event Action CloseEvents; //close events!

        protected void CallCloseEvents()
        {
            CloseEvents?.Invoke();
        }
        protected void CallRunEvents()
        {
            RunEvents?.Invoke();
        }
        public void AddRunEvent(Action task)
        {
            RunEvents+=task;
        }
        public void AddCloseEvent(Action task)
        {
            CloseEvents+=task;
        }

        public abstract Task Run(float simulationTimeLimit);

        public virtual DateTime GetRunStartTime()
        {
            return DateTime.Now;
        }

        protected event Action<NetworkMessage>? MessageSendAttempted;
        protected event Action<NetworkMessage>? MessageSendScheduled;
        protected event Action<NetworkMessage>? MessageLost;
        protected event Action<NetworkMessage>? MessageDelivered;

        protected void CallMessageSendAttempted(NetworkMessage message)
        {
            MessageSendAttempted?.Invoke(message);
        }
        protected void CallMessageSendScheduled(NetworkMessage message)
        {
            MessageSendScheduled?.Invoke(message);
        }
        protected void CallMessageLost(NetworkMessage message)
        {
            MessageLost?.Invoke(message);
        }
        protected void CallMessageDelivered(NetworkMessage message)
        {
            MessageDelivered?.Invoke(message);
        }



        public void AddMessageSendAttemptedHandler(Action<NetworkMessage> handler)
        {
            MessageSendAttempted += handler;

        }
        public void RemoveMessageSendAttemptedHandler(Action<NetworkMessage> handler)
        {
            MessageSendAttempted -= handler;

        }
        public void AddMessageSendScheduledHandler(Action<NetworkMessage> handler)
        {
            MessageSendScheduled += handler;

        }
        public void RemoveMessageSendScheduledHandler(Action<NetworkMessage> handler)
        {
            MessageSendScheduled -= handler;

        }

        public void AddMessageLostHandler(Action<NetworkMessage> handler)
        {
            MessageLost += handler;

        }
        public void RemoveMessageLostHandler(Action<NetworkMessage> handler)
        {
            MessageLost -= handler;
        }
        public void AddMessageDeliveredHandler(Action<NetworkMessage> handler)
        {
            MessageDelivered += handler;
        }
        public void RemoveMessageDeliveredHandler(Action<NetworkMessage> handler)
        {
            MessageDelivered -= handler;
        }

        protected Dictionary<ReplicaID, List<Action<NetworkMessage>>> messageDeliveredHandlers = new Dictionary<ReplicaID, List<Action<NetworkMessage>>>();

        protected Dictionary<ReplicaID, List<Action<ReplicaID>>> connectionInterruptedHandlers = new Dictionary<ReplicaID, List<Action<ReplicaID>>>();

        public ConnectionMap ConnectionMap { get; private set; }


        public NetworkSimulator(ConnectionMap connectionMap)
        {
            ConnectionMap = connectionMap;
        }


        public void AddConnectionInterruptedHandler(ReplicaID source, Action<ReplicaID> handler)
        {
            lock (connectionInterruptedHandlers)
            {
                if (!connectionInterruptedHandlers.ContainsKey(source))
                {
                    connectionInterruptedHandlers[source] = new List<Action<ReplicaID>>();
                }
                connectionInterruptedHandlers[source].Add(handler);
            }
        }

        public void RemoveConnectionInterruptedHandler(ReplicaID source, Action<ReplicaID> handler)
        {
            lock (connectionInterruptedHandlers)
            {
                connectionInterruptedHandlers[source].Remove(handler);
            }
        }

        public void AddMessageDeliveredHandler(ReplicaID id, Action<NetworkMessage> handler)
        {
            lock (messageDeliveredHandlers)
            {
                if (!messageDeliveredHandlers.ContainsKey(id))
                {
                    messageDeliveredHandlers[id] = new List<Action<NetworkMessage>>();
                }
                messageDeliveredHandlers[id].Add(handler);
            }
        }

        public void RemoveMessageDeliveredHandler(ReplicaID id, Action<NetworkMessage> handler)
        {
            lock (messageDeliveredHandlers)
            {
                if (messageDeliveredHandlers.ContainsKey(id))
                {
                    messageDeliveredHandlers[id].Remove(handler);
                }
            }
        }


        public abstract void ForceStop();

        protected void DeliverMessage(NetworkMessage message)
        {
            // Debug.WriteLine("sending " +message.Payload +" from "+message.Source.ToString()+" to " + message.Recipient.ToString());

            lock (ConnectionMap)
            {
                if (ConnectionMap.IsConnectionAvailable(message.Source, message.Recipient))
                {
                    bool shouldProceed;
                    List<Action<NetworkMessage>> handlers;
                    lock (messageDeliveredHandlers)
                    {
                        shouldProceed = messageDeliveredHandlers.TryGetValue(message.Recipient, out handlers);
                    }

                    if (shouldProceed)
                    {
                        foreach (var handler in handlers)
                        {
                            handler(message);
                        }
                        MessageDelivered?.Invoke(message);
                    }
                }
            }
        }



        public abstract bool ScheduleMessageDelivery(NetworkMessage message);

        public abstract void ExecuteAfter(float time, Action action);



        protected abstract void DropCommunicationsBetween(ReplicaID rep1, ReplicaID rep2);
        protected abstract void ChangeTimeOfCommunicationsBetween(ReplicaID rep1, ReplicaID rep2, float oldTime, float newTime);

        public void SetConnectionLatency(ReplicaID rep1, ReplicaID rep2, float time)
        {

            lock (ConnectionMap)
            {
             var con = ConnectionMap.GetConnection(rep1, rep2);
            if (con != null)
            {
                var latenctBefore = con.TransmissionTime;
                con.TransmissionTime = time;
                ChangeTimeOfCommunicationsBetween(rep1, rep2, latenctBefore, time);

            }
            }


        }
        public void TurnOffConnection(ReplicaID rep1, ReplicaID rep2)
        {
            lock (ConnectionMap)
            {
                var con = ConnectionMap.GetConnection(rep1, rep2);
                if (con != null)
                {
                    con.Active = false;
                    DropCommunicationsBetween(rep1, rep2);
                }
            }
        }
        public void TurnOnConnection(ReplicaID rep1, ReplicaID rep2)
        {
            lock (ConnectionMap)
            {
                var con = ConnectionMap.GetConnection(rep1, rep2);
                if (con != null)
                {
                    con.Active = true;
                }
            }
        }

        public HashSet<ReplicaID> GetAllKnownReplicas()
        {
            lock (ConnectionMap) { 
                return ConnectionMap.GetAllKnownReplicas();
            }
        }


        public abstract DateTime GetCurrentTime();
    }
}
