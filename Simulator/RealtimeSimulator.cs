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
        List<Func<Task>> runEvents;

        public override async Task<(bool, float)> Run(float simulationTimeLimit)
        {
            var start = DateTime.Now;

            List<Task> tasks = new List<Task>();
            foreach (var ev in runEvents)
            {
                tasks.Add(ev());
            }
            var delay = Task.Delay(TimeSpan.FromSeconds(simulationTimeLimit));
            var t = await Task.WhenAny(Task.WhenAll(tasks), delay);

            var end = DateTime.Now;
            var timespan = (end - start);
            float duration = (float)(timespan.TotalSeconds);

            return (t == delay, duration);


        }

        public RealtimeSimulator(List<Func<Task>> runEvents, ConnectionMap connectionMap) : base(connectionMap)
        {
            this.runEvents = runEvents;
        }



        public override bool ScheduleMessageDelivery(NetworkMessage message)
        {
            CallMessageSendAttempted(message);
            if (ConnectionMap.IsConnectionAvailable(message.Source, message.Recipient))
            {
                var connection = ConnectionMap.GetConnection(message.Source, message.Recipient);
                if (connection != null)
                {
                    ExecuteAfter(connection.TransmissionTime, () => { DeliverMessage(message); });
                    CallMessageSendScheduled(message);
                    return true;
                }
            }

            return false;

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
    }
}
