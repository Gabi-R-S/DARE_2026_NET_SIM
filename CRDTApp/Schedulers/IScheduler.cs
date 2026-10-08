using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Schedulers
{
    public interface IScheduler
    {
        public void ExecuteAfter(float time, Action action);


        public Task<(bool, T)> ExecuteTaskOrTimeout<T>(
            Func<Task<T>> operation,
            float time,
            T defaultValue);

        public Task<bool> ExecuteTaskOrTimeout(
           Func<Task> operation,
           float time);


        public Task<bool> ExecuteTaskOrTimeout(
           Func<Task> operation,
           float time,
           Action timeoutAction);

        public DateTime GetCurrentTime();
        public TimeSpan GetTimeElapsedSince(DateTime date)
        {
            return GetCurrentTime() - date;
        }
    }
}
