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
    internal class CompleteGraphExperiment : Experiment // assumes no broken connections, and no lost messages
    {
        public CompleteGraphExperiment(float maxExperimentDuration = 100.0f, float delay = 0.0f, int opsPerReplica = 1000, int numReplicas= 10, float delayBetweenSends=0.4f, float delayBetweenOps=0.1f,string prefix="")
        {

            this.delay = delay;
            this.maxExperimentDuration = maxExperimentDuration;
            this.opsPerReplica = opsPerReplica;
            this.numReplicas = numReplicas;
            this.delayBetweenSends = delayBetweenSends;
            this.delayBetweenOps = delayBetweenOps;
            this.prefix = prefix;
        }
        string prefix;
        int opsPerReplica;
        float maxExperimentDuration;
        float delay;

        float delayBetweenSends, delayBetweenOps;

        int numReplicas = 10;

        public async override Task<List<ExperimentResults>> Run()
        {
            List<ExperimentResults> results = new();


            var CreateDiscreteSim = (ConnectionMap map) =>
            {
                Simulation sim = new ThreadSafeSimulation(); 
                return new DiscreteSimulator(sim, map);
            };


            var CreateMap = () => { return NetworkCreationFunctions.CreateFullyConectedNetwork(numReplicas, delay); };
            // Control case: no network stack.
            results.Add(await RunIndividualFillArraySimulation(prefix + "fully-connected-no-stack", CreateMap, NetworkCreationFunctions.CreateOnlyBaseLayer, CreateDiscreteSim,maxExperimentDuration,opsPerReplica,delayBetweenSends,delayBetweenOps, NetworkCreationFunctions.BasicMetricCreator));


            // Simple Case: full stack, no routing
            results.Add(await RunIndividualFillArraySimulation(prefix + "fully-connected-ack-no-routing", CreateMap, NetworkCreationFunctions.CreateBasicNoRouteStack, CreateDiscreteSim, maxExperimentDuration, opsPerReplica, delayBetweenSends, delayBetweenOps, NetworkCreationFunctions.BasicMetricCreator));

            
            // Flood routing case
            results.Add(await RunIndividualFillArraySimulation(prefix + "fully-connected-basic-hop-flood", CreateMap, NetworkCreationFunctions.CreateBasicFloodRoutingStack, CreateDiscreteSim, maxExperimentDuration, opsPerReplica, delayBetweenSends, delayBetweenOps, NetworkCreationFunctions.BasicMetricCreator));

            // Distance Vector case
            results.Add(await RunIndividualFillArraySimulation(prefix + "fully-connected-basic-distance-vector", CreateMap, NetworkCreationFunctions.CreateBasicDistanceVectorStack, CreateDiscreteSim,  maxExperimentDuration, opsPerReplica, delayBetweenSends, delayBetweenOps, NetworkCreationFunctions.BasicMetricCreator));

            // Distance Vector case
            results.Add(await RunIndividualFillArraySimulation(prefix + "\"fully-connected-basic-link-state", CreateMap, NetworkCreationFunctions.CreateBasicLinkStateStack, CreateDiscreteSim,  maxExperimentDuration, opsPerReplica, delayBetweenSends, delayBetweenOps, NetworkCreationFunctions.BasicMetricCreator));
            
            return results;
        }
    }
}
