using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
using CRDTApp.NetworkLayers;
using CRDTApp.Schedulers;
using Shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace CRDTApp.Behaviours
{
    public class HeartbeatBehaviour : Behaviour
    {
        event Action<ReplicaID>? ConnectionInterrupted;
        event Action<ReplicaID>? ConnectionRegained;

        public void AddConnectionInterruptedHandler(Action<ReplicaID> handler)
        {
            ConnectionInterrupted += handler;
        }
        public void RemoveConnectionInterruptedHandler(Action<ReplicaID> handler)
        {
            ConnectionInterrupted -= handler;
        }

        public void AddConnectionRegainedHandler(Action<ReplicaID> handler)
        {
            ConnectionRegained += handler;
        }

        public void RemoveConnectionRegainedHandler(Action<ReplicaID> handler)
        {
            ConnectionRegained -= handler;
        }


        string tag;
        NetworkLayer network;
        IsRunningTracker runningTracker;
        IScheduler scheduler;
        ReplicaID replicaID;
        float heartbeatTime;
        ActiveConnectionTracker connectionTracker;
        HashSet<ReplicaID> sendingTo = new HashSet<ReplicaID>();
        
        float timeoutTime;
        ConcurrentDictionary<ReplicaID, DateTime> lastCommunication = new();


        public HeartbeatBehaviour(ReplicaID replicaID, NetworkLayer network, IsRunningTracker runningTracker, IScheduler scheduler, ActiveConnectionTracker connectionTracker, float heartbeatTime, float timeoutTime, string tag = "heartbeat")
        {
            this.network = network;
            this.runningTracker = runningTracker;
            this.scheduler = scheduler;
            this.connectionTracker = connectionTracker;
            this.heartbeatTime = heartbeatTime;
            this.replicaID = replicaID;
            this.tag = tag;
            this.timeoutTime = timeoutTime;

            network.AddOnMessageReceivedHandler(OnMessageReceived);

        }



        void TimeoutReached(ReplicaID replica) {
            if (!runningTracker.IsRunning)
            {
                return;
            }
            if (connectionTracker.SetInnactive(replica) && replica != replicaID)
            {
                if (replica != replicaID && !sendingTo.Contains(replica))
                {
                    sendingTo.Add(replica);
                    Heartbeat(replica);
                }

                ConnectionInterrupted?.Invoke(replica);
            }
        }
        void TimeoutRestored(ReplicaID replica)
        {
            if (!runningTracker.IsRunning)
            {
                return;
            }
            if (connectionTracker.SetActive(replica) && replica != replicaID)
            {
                ConnectionRegained?.Invoke(replica);
            }
        }

        void SetTime(ReplicaID replica)
        {
            if (runningTracker.IsRunning)
            {

                float timeSinceTimeout = -1;
                var currentTime = scheduler.GetCurrentTime();
                if (lastCommunication.ContainsKey(replica))
                {
                    var lastTime = lastCommunication[replica];

                    timeSinceTimeout = (float)(currentTime - lastTime).TotalSeconds;
                }

                lastCommunication[replica] = currentTime;

                if (replica != replicaID)
                {
                    if ((timeSinceTimeout < 0 || timeSinceTimeout > timeoutTime))
                    {
                        TimeoutRestored(replica);
                    }
                    scheduler.ExecuteAfter(timeoutTime, () =>
                    {
                        if (lastCommunication[replica] == currentTime)
                        {
                            TimeoutReached(replica);
                        }
                    });
                }
            }
        }


        void OnMessageReceived(ReplicaID sender, Message _)
        {

            if (!runningTracker.IsRunning)
            {
                return;
            }

            SetTime(sender);
        }


        void Heartbeat(ReplicaID rep)
        {
            if (!runningTracker.IsRunning)
            {
                return;
            }
            var _ = network.SendMessage(rep, new Message(tag,new HeartbeatMessagePayload(scheduler.GetCurrentTime()).Encode()));

            scheduler.ExecuteAfter(heartbeatTime, () => { Heartbeat(rep); });
        }


        public override void Launch()
        {
            sendingTo = new HashSet<ReplicaID>();

            foreach (var replica in connectionTracker.AllConnections)
            {
                if (replica != replicaID)
                {
                    sendingTo.Add(replica);
                    Heartbeat(replica);
                }
            }
        }


    }




}
