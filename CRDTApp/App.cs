using CRDTApp.Behaviours;
using CRDTApp.NetworkLayers;
using CRDTApp.Schedulers;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp
{
    public class App
    {
        SemaphoreSlim? sem;
        public IScheduler Scheduler { get; private set; }
        public List<Behaviour> Behaviours { get; private set; }
        public List<NetworkLayer> NetworkLayer { get; private set; }

        IsRunningTracker running;
        public void Close()
        {
            running?.IsRunning = false;
            sem?.Release();
            
        }
        public App(IsRunningTracker running,List<Behaviour> behaviours, IScheduler scheduler, List<NetworkLayer> networkComponents)
        {
            this.Scheduler = scheduler;
            this.Behaviours = behaviours;
            this.NetworkLayer = networkComponents;
            this.running = running;
        }

        public async void Run(float executionTime = -1)
        {
            running.IsRunning = true;
            sem = new SemaphoreSlim(0);
            foreach (var behaviour in Behaviours)
            {
                behaviour.Launch();
            }
            foreach (var networkLayer in NetworkLayer)
            {
                networkLayer.Launch();
            }
        }

        public void Wait(){
            sem?.Wait();
        }

        public bool IsRunning()
        {
            return running?.IsRunning ?? false;
        }
    }
}
