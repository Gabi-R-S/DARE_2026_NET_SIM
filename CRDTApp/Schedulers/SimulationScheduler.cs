using Simulator;
using System;
using System.Threading;
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
            Func<CancellationToken, Task<T>> operation,
            float time,
            T defaultValue)
        {
            using var cts = new CancellationTokenSource();

            // The simulation controls when cancellation happens.
            sim.ExecuteAfter(time, () => cts.Cancel());

            try
            {
                T result = await operation(cts.Token);

                return (true, result);
            }
            catch (OperationCanceledException)
            {
                return (false, defaultValue);
            }
        }

        public async Task<bool> ExecuteTaskOrTimeout(
            Func<CancellationToken, Task> operation,
            float time)
        {
            using var cts = new CancellationTokenSource();

            // The simulation controls when cancellation happens.
            sim.ExecuteAfter(time, () => cts.Cancel());

            try
            {
                await operation(cts.Token);

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public async Task<bool> ExecuteTaskOrTimeout(
            Func<CancellationToken, Task> operation,
            float time,
            Action timeoutAction)
        {
            using var cts = new CancellationTokenSource();

            sim.ExecuteAfter(time, () =>
            {
                cts.Cancel();
            });

            try
            {
                await operation(cts.Token);

                return true;
            }
            catch (OperationCanceledException)
            {
                timeoutAction();
                return false;
            }
        }

        public DateTime GetCurrentTime()
        {
            return sim.GetCurrentTime();
        }
    }
}