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
    }
}
