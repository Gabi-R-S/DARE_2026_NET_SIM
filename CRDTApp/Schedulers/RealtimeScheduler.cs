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

            var res = Task.WaitAny(t,task);

            if (res == 0) {
                return (false, defaultValue);
            }

            return (true, task.Result);

        }
    }
}
