using System;
using System.Threading;
using System.Threading.Tasks;

namespace CRDTApp.Schedulers
{
    public class RealtimeScheduler : IScheduler
    {
        public void ExecuteAfter(float time, Action action)
        {
            _ = Task.Delay(TimeSpan.FromSeconds(time))
                .ContinueWith(
                    _ => action(),
                    TaskScheduler.Default);
        }

        public async Task<(bool, T)> ExecuteTaskOrTimeout<T>(
            Func<CancellationToken, Task<T>> operation,
            float time,
            T defaultValue)
        {
            using var cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(time));

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
            using var cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(time));

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
            using var cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(time));

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
            return DateTime.Now;
        }
    }
}