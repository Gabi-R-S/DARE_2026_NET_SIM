using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Schedulers
{
    public interface IScheduler
    {
        public void ExecuteAfter(float time, Action action);
        public Task<(bool, T)> ExecuteTaskOrTimeout<T>(Task<T> task, float time, T defaultValue);
        public Task<bool> ExecuteTaskOrTimeout(Task task, float time);
    }
}
