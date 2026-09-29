using Shared;
using SimSharp;
using System;
using System.Collections.Generic;
using System.Text;
using System.Timers;

namespace Simulator
{
    public class RealtimeSimulator : NetworkSimulator
    {

        public override async Task Run(float simulationTimeLimit)
        {
            var start = DateTime.Now;

            List<Task> tasks = new List<Task>();
            foreach (var ev in RunEvents)
            {
                tasks.Add(ev());
            }
            var delay = Task.Delay(TimeSpan.FromSeconds(simulationTimeLimit));
            var t = await Task.WhenAny(Task.WhenAll(tasks), delay);
            
            foreach (var ev in CloseEvents) {
                ev();
            }

        }

        public RealtimeSimulator(ConnectionMap connectionMap) : base(connectionMap)
        { }



        public override bool ScheduleMessageDelivery(NetworkMessage message)
        {
            CallMessageSendAttempted(message);
            if (ConnectionMap.IsConnectionAvailable(message.Source, message.Recipient))
            {
                var connection = ConnectionMap.GetConnection(message.Source, message.Recipient);
                if (connection != null)
                {
                    MessageEvent messageEvent = new MessageEvent();
                    messageEvent.Active = true;
                    messageEvent.LaunchTime = GetCurrentTime();
                    messageEvent.Message = message;

                    if (!connectionsToMessageEvents.ContainsKey(connection))
                    {
                        connectionsToMessageEvents[connection] = new List<MessageEvent>();
                    }
                    connectionsToMessageEvents[connection].Add(messageEvent);

                    ExecuteAfter(connection.TransmissionTime, () => { if (messageEvent.Active) { DeliverMessage(message); } connectionsToMessageEvents[connection].Remove(messageEvent); });
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
            System.Timers.Timer timer = new System.Timers.Timer();
            timer.Elapsed += (_, _) => { action(); };
            timer.Interval = time * 1000;
            timer.Enabled = true;
        }



        public override DateTime GetCurrentTime()
        {
            return DateTime.Now;
        }


        protected override void DropCommunicationsBetween(ReplicaID rep1, ReplicaID rep2)
        {
            var con = ConnectionMap.GetConnection(rep1, rep2);
            if (con != null)
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
        protected override void ChangeTimeOfCommunicationsBetween(ReplicaID rep1, ReplicaID rep2, float oldTime, float newTime)
        {
            var con = ConnectionMap.GetConnection(rep1, rep2);
            if (con != null)
            {
                if (connectionsToMessageEvents.ContainsKey(con))
                {
                    var currTime = GetCurrentTime();
                    foreach (var ev in connectionsToMessageEvents[con])
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

                        ExecuteAfter(missingTime, () => { if (newEv.Active) { DeliverMessage(newEv.Message); } connectionsToMessageEvents[con].Remove(newEv); });


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


