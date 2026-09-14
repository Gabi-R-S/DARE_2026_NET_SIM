using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp
{
    public interface IScheduler
    {
        public void ExecuteAfter(float time, Action action);
    }
}
