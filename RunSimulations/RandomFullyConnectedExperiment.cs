using SimSharp;
using Simulator;

namespace RunSimulations
{
    internal class RandomFullyConnectedExperiment : Experiment
    {
        public RandomFullyConnectedExperiment(
            string prefix = "",
            int numReplicas = 10,
            float minDelay = 0.0f,
            float maxDelay = 0.0f,
            float maxExperimentDuration = 100.0f,
            int seed = 42,
            double extraEdgeProbability = 0.15,
            bool runHopelessCases = false,
            InsertionExperimentParams? extraParams = null)
        {
            this.prefix = prefix;
            this.numReplicas = numReplicas;
            this.minDelay = minDelay;
            this.maxDelay = maxDelay;
            this.maxExperimentDuration = maxExperimentDuration;
            this.seed = seed;
            this.extraEdgeProbability = extraEdgeProbability;
            this.runHopelessCases = runHopelessCases;
            this.extraParams = extraParams ?? new InsertionExperimentParams(
                opsPerReplica: 1000,
                delayBetweenSends: 0.4f,
                delayBetweenOps: 0.1f,
                heartbeatTime: 0.1f);
        }

        private readonly string prefix;
        private readonly int numReplicas;
        private readonly float minDelay;
        private readonly float maxDelay;
        private readonly float maxExperimentDuration;
        private readonly int seed;
        private readonly double extraEdgeProbability;
        private readonly bool runHopelessCases;
        private readonly InsertionExperimentParams extraParams;

        public override List<ExperimentResults> Run()
        {
            List<ExperimentResults> results = new();

            var createDiscreteSim = (ConnectionMap map) =>
            {
                Simulation sim = new ThreadSafeSimulation();
                return new DiscreteSimulator(sim, map);
            };

            var createMap = () => NetworkCreationFunctions.CreateRandomFullyConnectedNetwork(
                numReplicas, minDelay, maxDelay, seed, extraEdgeProbability);

            if (runHopelessCases)
            {
                results.Add(RunIndividualFillArraySimulation(
                    prefix + "random-no-stack",
                    createMap,
                    RoutingAlgorithmType.NONE,
                    createDiscreteSim,
                    maxExperimentDuration,
                    NetworkCreationFunctions.BasicMetricCreator,
                    extraParams));

                results.Add(RunIndividualFillArraySimulation(
                    prefix + "random-ack-no-routing",
                    createMap,
                    RoutingAlgorithmType.NONE_WITH_HEARTBEAT,
                    createDiscreteSim,
                    maxExperimentDuration,
                    NetworkCreationFunctions.BasicMetricCreator,
                    extraParams));
            }

            results.Add(RunIndividualFillArraySimulation(
                prefix + "random-basic-hop-flood",
                createMap,
                RoutingAlgorithmType.FLOODING,
                createDiscreteSim,
                maxExperimentDuration,
                NetworkCreationFunctions.BasicMetricCreator,
                extraParams));

            results.Add(RunIndividualFillArraySimulation(
                prefix + "random-basic-distance-vector",
                createMap,
                RoutingAlgorithmType.DISTANCE_VECTOR,
                createDiscreteSim,
                maxExperimentDuration,
                NetworkCreationFunctions.BasicMetricCreator,
                extraParams));

            results.Add(RunIndividualFillArraySimulation(
                prefix + "random-basic-link-state",
                createMap,
                RoutingAlgorithmType.LINK_STATE,
                createDiscreteSim,
                maxExperimentDuration,
                NetworkCreationFunctions.BasicMetricCreator,
                extraParams));

            return results;
        }
    }
}
