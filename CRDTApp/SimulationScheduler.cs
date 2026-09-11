using SimSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp
{
    internal class SimulationScheduler:IScheduler
    {
        Simulation env;
        public SimulationScheduler(Simulation simulation) {
            env = simulation;
        }

        public void ExecuteAfter(float time, Action action)
        {
            var actionEvent = new Event(env);
            actionEvent.AddCallback(e => action());
            env.ScheduleD(time, actionEvent);
        }



    }
}
