using SimSharp;
using Simulator;

namespace RunSimulations
{
    internal class ScaleFreeNetworkExperiment : Experiment
    {
        public ScaleFreeNetworkExperiment(
            string prefix = "",
            int numReplicas = 10,
            float minDelay = 0.0f,
            float maxDelay = 0.0f,
            float maxExperimentDuration = 100.0f,
            int seed = 42,
            int edgesPerNewNode = 2,
            bool runHopelessCases = false,
            InsertionExperimentParams? extraParams = null)
        {
            this.prefix = prefix;
            this.numReplicas = numReplicas;
            this.minDelay = minDelay;
            this.maxDelay = maxDelay;
            this.maxExperimentDuration = maxExperimentDuration;
            this.seed = seed;
            this.edgesPerNewNode = edgesPerNewNode;
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
        private readonly int edgesPerNewNode;
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

            var createMap = () => NetworkCreationFunctions.CreateScaleFreeNetwork(
                numReplicas, minDelay, maxDelay, seed, edgesPerNewNode);

            if (runHopelessCases)
            {
                results.Add(RunIndividualFillArraySimulation(
                    prefix + "scale-free-no-stack",
                    createMap,
                    RoutingAlgorithmType.NONE,
                    createDiscreteSim,
                    maxExperimentDuration,
                    NetworkCreationFunctions.BasicMetricCreator,
                    extraParams));

                results.Add(RunIndividualFillArraySimulation(
                    prefix + "scale-free-ack-no-routing",
                    createMap,
                    RoutingAlgorithmType.NONE_WITH_HEARTBEAT,
                    createDiscreteSim,
                    maxExperimentDuration,
                    NetworkCreationFunctions.BasicMetricCreator,
                    extraParams));
            }

            results.Add(RunIndividualFillArraySimulation(
                prefix + "scale-free-basic-hop-flood",
                createMap,
                RoutingAlgorithmType.FLOODING,
                createDiscreteSim,
                maxExperimentDuration,
                NetworkCreationFunctions.BasicMetricCreator,
                extraParams));

            results.Add(RunIndividualFillArraySimulation(
                prefix + "scale-free-basic-distance-vector",
                createMap,
                RoutingAlgorithmType.DISTANCE_VECTOR,
                createDiscreteSim,
                maxExperimentDuration,
                NetworkCreationFunctions.BasicMetricCreator,
                extraParams));

            results.Add(RunIndividualFillArraySimulation(
                prefix + "scale-free-basic-link-state",
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
