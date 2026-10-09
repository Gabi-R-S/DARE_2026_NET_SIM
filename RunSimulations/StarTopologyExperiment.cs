using SimSharp;
using Simulator;

namespace RunSimulations
{
    internal class StarTopologyExperiment : Experiment
    {
        public StarTopologyExperiment(
            string prefix = "",
            int numReplicas = 10,
            int centerNode = 0,
            float delay = 0.0f,
            float maxExperimentDuration = 100.0f,
            bool runHopelessCases = false,
            InsertionExperimentParams? extraParams = null)
        {
            this.prefix = prefix;
            this.numReplicas = numReplicas;
            this.centerNode = centerNode;
            this.delay = delay;
            this.maxExperimentDuration = maxExperimentDuration;
            this.runHopelessCases = runHopelessCases;
            this.extraParams = extraParams ?? new InsertionExperimentParams(
                opsPerReplica: 1000,
                delayBetweenSends: 0.4f,
                delayBetweenOps: 0.1f,
                heartbeatTime: 0.1f);
        }

        private readonly string prefix;
        private readonly int numReplicas;
        private readonly int centerNode;
        private readonly float delay;
        private readonly float maxExperimentDuration;
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

            var createMap = () => NetworkCreationFunctions.CreateStarNetwork(
                numReplicas, centerNode, delay);

            if (runHopelessCases)
            {
                results.Add(RunIndividualFillArraySimulation(
                    prefix + "star-no-stack",
                    createMap,
                    RoutingAlgorithmType.NONE,
                    createDiscreteSim,
                    maxExperimentDuration,
                    NetworkCreationFunctions.BasicMetricCreator,
                    extraParams));

                results.Add(RunIndividualFillArraySimulation(
                    prefix + "star-ack-no-routing",
                    createMap,
                    RoutingAlgorithmType.NONE_WITH_HEARTBEAT,
                    createDiscreteSim,
                    maxExperimentDuration,
                    NetworkCreationFunctions.BasicMetricCreator,
                    extraParams));
            }

            results.Add(RunIndividualFillArraySimulation(
                prefix + "star-basic-hop-flood",
                createMap,
                RoutingAlgorithmType.FLOODING,
                createDiscreteSim,
                maxExperimentDuration,
                NetworkCreationFunctions.BasicMetricCreator,
                extraParams));

            results.Add(RunIndividualFillArraySimulation(
                prefix + "star-basic-distance-vector",
                createMap,
                RoutingAlgorithmType.DISTANCE_VECTOR,
                createDiscreteSim,
                maxExperimentDuration,
                NetworkCreationFunctions.BasicMetricCreator,
                extraParams));

            results.Add(RunIndividualFillArraySimulation(
                prefix + "star-basic-link-state",
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
