using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp
{
    internal interface IScheduler
    {
        public void ExecuteAfter(float time, Action action);
    }
}
