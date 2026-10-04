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

        public IScheduler Scheduler { get; private set; }
        public Behaviour Behaviour { get; private set; }
        public NetworkLayer NetworkComponent { get; private set; }
        public void Close() {
            Behaviour.Stop();
            NetworkComponent.Stop();
        }
        public App(Behaviour behaviour, IScheduler scheduler, NetworkLayer networkComponent)
        {
            this.Scheduler = scheduler;
            this.Behaviour = behaviour;
            this.NetworkComponent = networkComponent;
        }

        public async Task Run(float executionTime =-1)
        {
            var t1 = Behaviour.Run();
            var t2 =NetworkComponent.Run();
            await Task.WhenAll(t1, t2);
        }
    }
}
