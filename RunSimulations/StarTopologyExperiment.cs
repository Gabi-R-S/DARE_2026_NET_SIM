using CRDTApp;
using CRDTApp.Behaviours;
using CRDTApp.NetworkLayers;
using CRDTApp.Schedulers;
using Shared;
using SimSharp;
using Simulator;
using System;
using System.Collections.Generic;
using System.Text;

namespace RunSimulations
{
    internal class StarTopologyExperiment : Experiment // assumes no broken connections, and no lost messages
    {
        public StarTopologyExperiment(float maxExperimentDuration = 100.0f, float delay = 0.0f, int opsPerReplica = 1000, int numReplicas = 10)
        {

            this.delay = delay;
            this.maxExperimentDuration = maxExperimentDuration;
            this.opsPerReplica = opsPerReplica;
            this.numReplicas = numReplicas;
        }

        int opsPerReplica;
        float maxExperimentDuration;
        float delay;

        int numReplicas;
        public override List<ExperimentResults> Run()
        {
            List<ExperimentResults> results = new();


            var CreateDiscreteSim = (ConnectionMap map) =>
            {
                Simulation sim = new ThreadSafeSimulation();
                return new DiscreteSimulator(sim, map);
            };


            var CreateMap = () => { return CreateStarNetwork(this.numReplicas,0,delay); };
            // Control case: no network stack.
            results.Add(RunIndividualFillArraySimulation("No network stack", CreateMap, CreateOnlyBaseLayer, CreateDiscreteSim, maxExperimentDuration, opsPerReplica));


            // Simple Case: full stack, no routing
            results.Add(RunIndividualFillArraySimulation("ack layer + no routing", CreateMap, CreateBasicNoRouteStack, CreateDiscreteSim, maxExperimentDuration, opsPerReplica));


            // Flood routing case
            results.Add(RunIndividualFillArraySimulation("ack layer + flood routing with max hop count", CreateMap, CreateBasicFloodRoutingStack, CreateDiscreteSim, maxExperimentDuration, opsPerReplica));

            // Distance Vector case
            results.Add(RunIndividualFillArraySimulation("ack + distance vector", CreateMap, CreateBasicDistanceVectorStack, CreateDiscreteSim, maxExperimentDuration, opsPerReplica));

            // Distance Vector case
            results.Add(RunIndividualFillArraySimulation("ack + link state", CreateMap, CreateBasicLinkStateStack, CreateDiscreteSim, maxExperimentDuration, opsPerReplica));



            return results;
        }
    }
}
