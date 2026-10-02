using SimSharp;
using Simulator;
using System;
using System.Collections.Generic;
using System.Text;

namespace RunSimulations
{
    internal class RandomFullyConnectedExperiment:Experiment
    {

        public RandomFullyConnectedExperiment(float maxExperimentDuration = 100.0f, float minDelay = 0.0f, float maxDelay = 0.0f, int opsPerReplica = 1000, int numReplicas = 10, bool runHopelessCases = false, float delayBetweenSends = 0.4f, float delayBetweenOps = 0.1f,int seed = 42)
        {

            this.minDelay = minDelay;
            this.maxDelay = maxDelay;
            this.maxExperimentDuration = maxExperimentDuration;
            this.opsPerReplica = opsPerReplica;
            this.numReplicas = numReplicas;
            this.runHopelessCases = runHopelessCases;
            this.delayBetweenOps = delayBetweenOps;
            this.delayBetweenSends = delayBetweenSends;
            this.seed = seed;
        }

        int opsPerReplica;
        float maxExperimentDuration;
        float minDelay;
        float maxDelay;

        int seed = 42;
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
            

            var CreateMap = () => { return CreateRandomFullyConnectedNetwork(this.numReplicas, minDelay, maxDelay, seed); };

            if (runHopelessCases)
            {
                // Control case: no network stack.
                results.Add(RunIndividualFillArraySimulation("random-no-stack", CreateMap, CreateOnlyBaseLayer, CreateDiscreteSim, maxExperimentDuration, opsPerReplica, delayBetweenSends, delayBetweenOps, BasicMetricCreator));


                // Simple Case: full stack, no routing
                results.Add(RunIndividualFillArraySimulation("random-ack-no-routing", CreateMap, CreateBasicNoRouteStack, CreateDiscreteSim, maxExperimentDuration, opsPerReplica, delayBetweenSends, delayBetweenOps, BasicMetricCreator));

            }
            // Flood routing case
            results.Add(RunIndividualFillArraySimulation("random-basic-hop-flood", CreateMap, CreateBasicFloodRoutingStack, CreateDiscreteSim, maxExperimentDuration, opsPerReplica, delayBetweenSends, delayBetweenOps, BasicMetricCreator));

            // Distance Vector case
            results.Add(RunIndividualFillArraySimulation("random-basic-distance-vector", CreateMap, CreateBasicDistanceVectorStack, CreateDiscreteSim, maxExperimentDuration, opsPerReplica, delayBetweenSends, delayBetweenOps, BasicMetricCreator));

            // Distance Vector case
            results.Add(RunIndividualFillArraySimulation("random-basic-link-state", CreateMap, CreateBasicLinkStateStack, CreateDiscreteSim, maxExperimentDuration, opsPerReplica, delayBetweenSends, delayBetweenOps, BasicMetricCreator));



            return results;
        }
    }
}
