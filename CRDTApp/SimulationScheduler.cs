using Simulator;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp
{
    public class SimulationScheduler:IScheduler
    {
        NetworkSimulator sim;
        public SimulationScheduler(NetworkSimulator sim) {
            this.sim = sim;
        }

        public void ExecuteAfter(float time, Action action)
        {
            sim.ExecuteAfter(time,action);
        }



    }
}
