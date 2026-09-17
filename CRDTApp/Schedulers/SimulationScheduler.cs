using Simulator;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Schedulers
{
    public class SimulationScheduler:IScheduler
    {
        NetworkSimulator sim;
        public SimulationScheduler(NetworkSimulator sim) {
            this.sim = sim;
        }

        public void ExecuteAfter(float time, Action action)
        {
            sim.ExecuteAfter(time,action);
        }



        public async Task<(bool, T)> ExecuteTaskOrTimeout<T>(Task<T> task, float time, T defaultValue) {
            SemaphoreSlim sem = new SemaphoreSlim(0);

            sim.ExecuteAfter(time, () => { sem.Release(); });

            var ind = Task.WaitAny(sem.WaitAsync(),task);

            if (ind == 0) {
                return (false, defaultValue);
            }

            return (true, task.Result);

        }

        public async Task<bool> ExecuteTaskOrTimeout(Task task, float time)
        {
            SemaphoreSlim sem = new SemaphoreSlim(0);

            sim.ExecuteAfter(time, () => { sem.Release(); });

            var ind = Task.WaitAny(sem.WaitAsync(), task);

            if (ind == 0)
            {
                return false;
            }

            return true;

        }

    }
}
