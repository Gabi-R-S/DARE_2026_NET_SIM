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
                    Event ev = new Event(env);
                    ev.AddCallback((env) =>
                    {
                        DeliverMessage(message);
                    });

                    env.ScheduleD(connection.TransmissionTime, ev);
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
    }
}
