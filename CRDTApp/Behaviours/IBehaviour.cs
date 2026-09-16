using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Behaviours
{
    public interface IBehaviour
    {
        public Task Run();
    }
}
