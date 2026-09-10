using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp
{
    internal class TestBehaviour1 :IBehaviour
    {
        public async Task Run()
        {
            Console.WriteLine("TestBehaviour1 is running.");
        }
    }
}
