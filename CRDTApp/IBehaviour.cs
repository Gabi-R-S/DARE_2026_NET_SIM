using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp
{
    public interface IBehaviour
    {
        public Task Run();
    }
}
