using System;
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
            Func<Task<T>> operation,
            float time,
            T defaultValue)
        {
            Task<T> operationTask = operation();
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(time));

            if (await Task.WhenAny(operationTask, timeoutTask) != operationTask)
            {
                return (false, defaultValue);
            }

            return (true, await operationTask);
        }

        public async Task<bool> ExecuteTaskOrTimeout(
            Func<Task> operation,
            float time)
        {
            Task operationTask = operation();
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(time));

            if (await Task.WhenAny(operationTask, timeoutTask) != operationTask)
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
            Task operationTask = operation();
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(time));

            if (await Task.WhenAny(operationTask, timeoutTask) != operationTask)
            {
                timeoutAction();
                return false;
            }

            await operationTask;
            return true;
        }

        public DateTime GetCurrentTime()
        {
            return DateTime.Now;
        }
    }
}