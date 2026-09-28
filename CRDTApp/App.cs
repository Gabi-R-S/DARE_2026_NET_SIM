using CRDTApp.Behaviours;
using CRDTApp.NetworkLayers;
using CRDTApp.Schedulers;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp
{
    public class App
    {
        SemaphoreSlim closeSem = new SemaphoreSlim(0);

        public void Close() {
            closeSem.Release();
        }
        public App()
        {
        }

        public async Task Run(IBehaviour behaviour,  IScheduler? scheduler = null, NetworkLayer network = null, float executionTime =-1)
        {
            var t1 = behaviour.Run();
            var t2 =network?.Run();

            var wait = Task.WhenAny(t1, closeSem.WaitAsync());
            
            if (scheduler != null && executionTime > 0) {
                scheduler.ExecuteAfter(executionTime, Close);
                await scheduler.ExecuteTaskOrTimeout(wait, executionTime);
            }
            else {
                await wait;
            }
        }
    }
}
