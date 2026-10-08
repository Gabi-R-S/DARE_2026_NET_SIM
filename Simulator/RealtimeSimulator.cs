using Shared;
using SimSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Timers;

namespace Simulator
{
    public class RealtimeSimulator : NetworkSimulator
    {
        volatile bool running = false;
        public override void ForceStop()
        {
            Debug.WriteLine("THE ULTIMATE STOP WAS CALLED!!!!!!!!!!!!!!!!!!!!!!!!!!!");
            running = false;
        }
        DateTime start;
        public override async Task Run(float simulationTimeLimit)
        {
            start = DateTime.Now;

            List<Task> tasks = new List<Task>();
            running = true;
            foreach (var ev in RunEvents)
            {
                tasks.Add(ev());
            }
            var delay = simulationTimeLimit > 0
                ? Task.Delay(TimeSpan.FromSeconds(simulationTimeLimit))
                : Task.Delay(System.Threading.Timeout.InfiniteTimeSpan);
            
            await delay;

            tasks.Clear();
            foreach (var ev in CloseEvents) {
                tasks.Add(ev());
            }

            await Task.WhenAll(tasks);
            running = false;
        }

        public override DateTime GetRunStartTime()
        {
            return start;
        }

        public RealtimeSimulator(ConnectionMap connectionMap) : base(connectionMap)
        { }



        public override bool ScheduleMessageDelivery(NetworkMessage message)
        {
            if (!running)
            {
                return false;
            }
            CallMessageSendAttempted(message);


            if (message.Source == message.Recipient)
            {
                ExecuteAfter(0, () => { DeliverMessage(message);});
                CallMessageSendScheduled(message);

                return true;
            }

            if (ConnectionMap.IsConnectionAvailable(message.Source, message.Recipient))
            {
                var connection = ConnectionMap.GetConnection(message.Source, message.Recipient);
                if (connection != null)
                {
                    MessageEvent messageEvent = new MessageEvent();
                    messageEvent.Active = true;
                    messageEvent.LaunchTime = GetCurrentTime();
                    messageEvent.Message = message;

                    lock (connectionsToMessageEvents)
                    {
                        if (!connectionsToMessageEvents.ContainsKey(connection))
                        {
                            connectionsToMessageEvents[connection] = new List<MessageEvent>();
                        }
                        connectionsToMessageEvents[connection].Add(messageEvent);
                    }

                    ExecuteAfter(connection.TransmissionTime, () =>
                    {
                        if (messageEvent.Active) { DeliverMessage(message); }
                        lock (connectionsToMessageEvents)
                        {
                            connectionsToMessageEvents[connection].Remove(messageEvent);
                        }
                    });
                    CallMessageSendScheduled(message);
                    return true;
                }
            }

            return false;

        }


        Dictionary<NetworkConnection, List<MessageEvent>> connectionsToMessageEvents = new();
        class MessageEvent
        {
            public bool Active { get; set; } = true;
            public NetworkMessage Message { get; set; }
            public DateTime LaunchTime { get; set; }

        }




        public override void ExecuteAfter(float time, Action action)
        {
            if (!running)
            {
                return;
            }

            if (time <= 0)
            {
                Task.Run(action);
                return;
            }

            System.Timers.Timer timer = new System.Timers.Timer();
            timer.AutoReset = false;
            timer.Elapsed += (_, _) =>
            {
                timer.Dispose();
                if (running)
                {
                    action();
                }
            };
            timer.Interval = time * 1000;
            timer.Enabled = true;
        }



        public override DateTime GetCurrentTime()
        {
            return DateTime.Now;
        }


        protected override void DropCommunicationsBetween(ReplicaID rep1, ReplicaID rep2)
        {
            if(!running)
            {
                return;
            }
            var con = ConnectionMap.GetConnection(rep1, rep2);
            if (con != null)
            {
                lock (connectionsToMessageEvents)
                {
                    if (connectionsToMessageEvents.ContainsKey(con))
                    {
                        foreach (var ev in connectionsToMessageEvents[con])
                        {
                            ev.Active = false;
                        }
                    }
                    else
                    {
                        connectionsToMessageEvents[con] = new();
                    }
                }
            }
        }
        protected override void ChangeTimeOfCommunicationsBetween(ReplicaID rep1, ReplicaID rep2, float oldTime, float newTime)
        {
            if (!running)
            {
                return;
            }

            var con = ConnectionMap.GetConnection(rep1, rep2);
            if (con != null)
            {
                lock (connectionsToMessageEvents)
                {
                    if (connectionsToMessageEvents.ContainsKey(con))
                    {
                        var currTime = GetCurrentTime();
                        foreach (var ev in connectionsToMessageEvents[con].ToArray())
                        {
                            ev.Active = false;
                            float timeRunning = (float)(currTime - ev.LaunchTime).TotalSeconds;
                            float timePercentage = oldTime == 0 ? 1 : timeRunning / oldTime;

                            float missingTime = (1 - timePercentage) * newTime;

                            MessageEvent newEv = new MessageEvent();
                            connectionsToMessageEvents[con].Add(newEv);
                            newEv.Active = true;
                            newEv.LaunchTime = GetCurrentTime();
                            newEv.Message = ev.Message;

                            ExecuteAfter(missingTime, () =>
                            {
                                if (newEv.Active) { DeliverMessage(newEv.Message); }
                                lock (connectionsToMessageEvents)
                                {
                                    connectionsToMessageEvents[con].Remove(newEv);
                                }
                            });
                        }
                    }
                    else
                    {
                        connectionsToMessageEvents[con] = new();
                    }
                }
            }
        }
    }
}


