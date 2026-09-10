using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp
{
    internal interface IBehaviour
    {
        public Task Run();
    }
}
