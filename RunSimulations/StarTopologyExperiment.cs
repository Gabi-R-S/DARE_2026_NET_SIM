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
        public StarTopologyExperiment(float maxExperimentDuration = 100.0f, float delay = 0.0f, int opsPerReplica = 1000, int numReplicas = 10, bool runHopelessCases=false, float delayBetweenSends = 0.4f, float delayBetweenOps = 0.1f)
        {

            this.delay = delay;
            this.maxExperimentDuration = maxExperimentDuration;
            this.opsPerReplica = opsPerReplica;
            this.numReplicas = numReplicas;
            this.runHopelessCases = runHopelessCases;
            this.delayBetweenSends = delayBetweenSends;
            this.delayBetweenOps = delayBetweenOps;
        }

        int opsPerReplica;
        float maxExperimentDuration;
        float delay;

        float delayBetweenSends, delayBetweenOps;
        int numReplicas;
        bool runHopelessCases = false;
        public override List<ExperimentResults> Run()
        {
            List<ExperimentResults> results = new();


            var CreateDiscreteSim = (ConnectionMap map) =>
            {
                Simulation sim = new ThreadSafeSimulation();
                return new DiscreteSimulator(sim, map);
            };


            var CreateMap = () => { return CreateStarNetwork(this.numReplicas,0,delay); };

            if (runHopelessCases)
            {
                // Control case: no network stack.
                results.Add(RunIndividualFillArraySimulation("star-no-stack", CreateMap, CreateOnlyBaseLayer, CreateDiscreteSim, maxExperimentDuration, opsPerReplica,delayBetweenSends,delayBetweenOps, BasicMetricCreator));


                // Simple Case: full stack, no routing
                results.Add(RunIndividualFillArraySimulation("star-ack-no-routing", CreateMap, CreateBasicNoRouteStack, CreateDiscreteSim, maxExperimentDuration, opsPerReplica, delayBetweenSends, delayBetweenOps, BasicMetricCreator));
            }

            // Flood routing case
            results.Add(RunIndividualFillArraySimulation("star-basic-hop-flood", CreateMap, CreateBasicFloodRoutingStack, CreateDiscreteSim, maxExperimentDuration, opsPerReplica, delayBetweenSends, delayBetweenOps, BasicMetricCreator));

            // Distance Vector case
            results.Add(RunIndividualFillArraySimulation("star-basic-distance-vector", CreateMap, CreateBasicDistanceVectorStack, CreateDiscreteSim, maxExperimentDuration, opsPerReplica, delayBetweenSends, delayBetweenOps, BasicMetricCreator));

            // Distance Vector case
            results.Add(RunIndividualFillArraySimulation("star-basic-link-state", CreateMap, CreateBasicLinkStateStack, CreateDiscreteSim, maxExperimentDuration, opsPerReplica, delayBetweenSends, delayBetweenOps, BasicMetricCreator));



            return results;
        }
    }
}
