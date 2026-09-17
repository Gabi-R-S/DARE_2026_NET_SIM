using CRDTApp.Behaviours;
using CRDTApp.NetworkManagers;
using CRDTApp.Schedulers;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp
{
    public class App
    {
        IBehaviour behaviour;
        NetworkLayer networkManager;
        SemaphoreSlim closeSem = new SemaphoreSlim(0);

        public void Close() {
            closeSem.Release();
        }
        public App(IBehaviour behaviour, NetworkLayer networkManager)
        {
            this.behaviour = behaviour;
            this.networkManager = networkManager;
        }

        public App(IBehaviour behaviour, NetworkLayer networkManager, IScheduler scheduler, float executionTime)
        {
            this.behaviour = behaviour;
            this.networkManager = networkManager;
            scheduler.ExecuteAfter(executionTime,Close);
        }
        public async Task Run()
        {
            var t1 = behaviour.Run();
            var t2 = networkManager.Run();

            await Task.WhenAny(Task.WhenAll(t1, t2), closeSem.WaitAsync());
        }
    }
}
