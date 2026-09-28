using Shared;
using SimSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace Simulator
{
    public class DiscreteSimulator : NetworkSimulator
    {

        const float MAX_SIM_TIME = 3600.0f;
        public override async Task<(bool, float)> Run(float simulationTimeLimit)
        {

            foreach (var runEvent in RunEvents) {
                runEvent();
            }
            var t= Task.Run(() => { env.Run(); });
            var t2 = await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(MAX_SIM_TIME)));


            if (t2 == t)
            {
                var start = env.StartDate;
                var end = env.Now;
                var timespan = (end - start);
                float duration = (float)(timespan.TotalSeconds);

                return (duration <= simulationTimeLimit, duration);
            }
            else {
                return (false, -1);//timeout
            }

        }

        Simulation env;

        public DiscreteSimulator(Simulation env, ConnectionMap connectionMap) : base(connectionMap)
        {
            this.env = env;
        }


        public override bool ScheduleMessageDelivery(NetworkMessage message)
        {
            CallMessageSendAttempted(message);
            if (ConnectionMap.IsConnectionAvailable(message.Source, message.Recipient))
            {
                var connection = ConnectionMap.GetConnection(message.Source, message.Recipient);
                if (connection != null)
                {
                    if (!connectionsToMessageEvents.ContainsKey(connection)) {
                        connectionsToMessageEvents[connection] = new List<MessageEvent>();
                    }

                    MessageEvent ev= new MessageEvent();
                    connectionsToMessageEvents[connection].Add(ev);
                    ev.Active = true;
                    ev.LaunchTime = GetCurrentTime();
                    ev.Ev = new Event(env);
                    ev.Message = message;
                    ev.Ev.AddCallback((env) =>
                    {
                        if (ev.Active) {
                            DeliverMessage(message);
                        }
                        connectionsToMessageEvents[connection].Remove(ev);
                    });

                    env.ScheduleD(connection.TransmissionTime, ev.Ev);
                    CallMessageSendScheduled(message);
                    return true;
                }
            }

            return false;

        }

        public override void ExecuteAfter(float time, Action action)
        {
            var actionEvent = new Event(env);
            actionEvent.AddCallback(e => action());
            env.ScheduleD(time, actionEvent);
        }



        public override DateTime GetCurrentTime() {
            return env.Now;
        }

        class MessageEvent {
            public bool Active { get; set; } = true;
            public Event Ev { get; set; }
            public NetworkMessage Message { get; set; }
            public DateTime LaunchTime { get; set;}

        }

        Dictionary<NetworkConnection, List<MessageEvent>> connectionsToMessageEvents = new(); 

        protected override void DropCommunicationsBetween(ReplicaID rep1, ReplicaID rep2) {
            var con = ConnectionMap.GetConnection(rep1, rep2);
            if (con != null) {
                if (connectionsToMessageEvents.ContainsKey(con)) {
                    foreach (var ev in connectionsToMessageEvents[con]) {
                        ev.Active = false;
                    }
                }
                else {
                    connectionsToMessageEvents[con] = new ();
                }
            }
        }
        protected override void ChangeTimeOfCommunicationsBetween(ReplicaID rep1, ReplicaID rep2, float oldTime, float newTime) {
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
                        float timePercentage = oldTime==0?1: timeRunning / oldTime;

                        float missingTime = (1 - timePercentage) * newTime;

                        MessageEvent newEv = new MessageEvent();
                        connectionsToMessageEvents[con].Add(newEv);
                        newEv.Active = true;
                        newEv.LaunchTime = GetCurrentTime();
                        newEv.Ev = new Event(env);
                        newEv.Message = ev.Message;
                        newEv.Ev.AddCallback((env) =>
                        {
                            if (newEv.Active)
                            {
                                DeliverMessage(newEv.Message);
                            }
                            connectionsToMessageEvents[con].Remove(newEv);
                        });

                        env.ScheduleD(missingTime, newEv.Ev);
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
