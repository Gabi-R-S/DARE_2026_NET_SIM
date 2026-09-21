using System;
using System.Collections.Generic;
using System.Text;
using System.Timers;

namespace CRDTApp.Schedulers
{
    public class RealtimeScheduler : IScheduler
    {

        public void ExecuteAfter(float time, Action action) {
            System.Timers.Timer timer = new System.Timers.Timer(time);
            timer.AutoReset = false;
            timer.Elapsed += (_,_) => action();
            timer.Start();
        }


        public async Task<(bool,T)> ExecuteTaskOrTimeout<T>(Task<T> task, float time, T defaultValue) {

            var t = Task.Delay((int)time*1000);

            var res = Task.WhenAny(t,task);

            if (res == t) {
                return (false, defaultValue);
            }

            return (true, task.Result);

        }

        public async Task<bool> ExecuteTaskOrTimeout(Task task, float time)
        {

            var t = Task.Delay((int)time * 1000);

            var res = Task.WhenAny(t, task);

            if (res == t)
            {
                return false;
            }

            return true;

        }

        public DateTime GetCurrentTime() {
            return DateTime.Now;
        }
    }
}
