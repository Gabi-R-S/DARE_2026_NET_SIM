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
        public CompleteGraphExperiment(float maxExperimentDuration = 100.0f, float delay = 0.0f, int opsPerReplica = 1000, int numReplicas= 10, float delayBetweenSends=0.4f, float delayBetweenOps=0.1f)
        {

            this.delay = delay;
            this.maxExperimentDuration = maxExperimentDuration;
            this.opsPerReplica = opsPerReplica;
            this.numReplicas = numReplicas;
            this.delayBetweenSends = delayBetweenSends;
            this.delayBetweenOps = delayBetweenOps;
        }

        int opsPerReplica;
        float maxExperimentDuration;
        float delay;

        float delayBetweenSends, delayBetweenOps;

        int numReplicas = 10;

        public override List<ExperimentResults> Run()
        {
            List<ExperimentResults> results = new();


            var CreateDiscreteSim = (ConnectionMap map) =>
            {
                Simulation sim = new ThreadSafeSimulation(); 
                return new DiscreteSimulator(sim, map);
            };


            var CreateMap = () => { return CreateFullyConectedNetwork(numReplicas, delay); };
            // Control case: no network stack.
            results.Add(RunIndividualFillArraySimulation("fully-connected-no-stack", CreateMap,CreateOnlyBaseLayer, CreateDiscreteSim,maxExperimentDuration,opsPerReplica,delayBetweenSends,delayBetweenOps, BasicMetricCreator));


            // Simple Case: full stack, no routing
            results.Add(RunIndividualFillArraySimulation("fully-connected-ack-no-routing", CreateMap, CreateBasicNoRouteStack, CreateDiscreteSim, maxExperimentDuration, opsPerReplica, delayBetweenSends, delayBetweenOps, BasicMetricCreator));

            
            // Flood routing case
            results.Add(RunIndividualFillArraySimulation("fully-connected-basic-hop-flood", CreateMap, CreateBasicFloodRoutingStack, CreateDiscreteSim, maxExperimentDuration, opsPerReplica, delayBetweenSends, delayBetweenOps, BasicMetricCreator));

            // Distance Vector case
            results.Add(RunIndividualFillArraySimulation("fully-connected-basic-distance-vector", CreateMap, CreateBasicDistanceVectorStack, CreateDiscreteSim,  maxExperimentDuration, opsPerReplica, delayBetweenSends, delayBetweenOps, BasicMetricCreator));

            // Distance Vector case
            results.Add(RunIndividualFillArraySimulation("\"fully-connected-basic-link-state", CreateMap, CreateBasicLinkStateStack, CreateDiscreteSim,  maxExperimentDuration, opsPerReplica, delayBetweenSends, delayBetweenOps, BasicMetricCreator));
            
            return results;
        }
    }
}
