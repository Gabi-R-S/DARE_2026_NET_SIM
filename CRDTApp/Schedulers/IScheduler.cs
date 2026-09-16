using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Schedulers
{
    public interface IScheduler
    {
        public void ExecuteAfter(float time, Action action);
    }
}
