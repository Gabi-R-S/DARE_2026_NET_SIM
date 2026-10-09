using Simulator;
using System;
using System.Threading.Tasks;

namespace CRDTApp.Schedulers
{
    public class SimulationScheduler : IScheduler
    {
        private readonly NetworkSimulator sim;

        public SimulationScheduler(NetworkSimulator sim)
        {
            this.sim = sim;
        }

        public void ExecuteAfter(float time, Action action)
        {
            sim.ExecuteAfter(time, action);
        }

        public async Task<(bool, T)> ExecuteTaskOrTimeout<T>(
            Func<Task<T>> operation,
            float time,
            T defaultValue)
        {
            var timeout = new TaskCompletionSource<bool>();
            sim.ExecuteAfter(time, () => timeout.TrySetResult(true));

            Task<T> operationTask = operation();
            if (await Task.WhenAny(operationTask, timeout.Task) != operationTask)
            {
                return (false, defaultValue);
            }

            return (true, await operationTask);
        }

        public async Task<bool> ExecuteTaskOrTimeout(
            Func<Task> operation,
            float time)
        {
            var timeout = new TaskCompletionSource<bool>();
            sim.ExecuteAfter(time, () => timeout.TrySetResult(true));

            Task operationTask = operation();
            if (await Task.WhenAny(operationTask, timeout.Task) != operationTask)
            {
                return false;
            }

            await operationTask;
            return true;
        }

        public async Task<bool> ExecuteTaskOrTimeout(
            Func<Task> operation,
            float time,
            Action timeoutAction)
        {
            var timeout = new TaskCompletionSource<bool>();
            sim.ExecuteAfter(time, () => timeout.TrySetResult(true));

            Task operationTask = operation();
            if (await Task.WhenAny(operationTask, timeout.Task) != operationTask)
            {
                timeoutAction();
                return false;
            }

            await operationTask;
            return true;
        }

        public DateTime GetCurrentTime()
        {
            return sim.GetCurrentTime();
        }
    }
}