using Newtonsoft.Json;

namespace RunSimulations
{
    internal class Program
    {
        private static readonly string[] Topologies =
        {
            "complete",
            "ring",
            "star",
            "random",
            "scale-free"
        };

        static void Main(string[] args)
        {
            var baseline = new SimulationSettings();
            var commonScenarios = CreateCommonScenarios(baseline);
            List<ExperimentResults> results = new();

            foreach (string topology in Topologies)
            {
                var scenarios = new List<(string Name, SimulationSettings Settings)>(commonScenarios);
                AddTopologySpecificScenarios(topology, baseline, scenarios);

                foreach (var (scenarioName, settings) in scenarios)
                {
                    Experiment experiment = CreateExperiment(
                        topology,
                        $"{scenarioName}-",
                        settings);
                    results.AddRange(experiment.Run());
                }
            }

            string outputPath = Path.GetFullPath(
                args.Length > 0 ? args[0] : "simulation-results.json");
            string? outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            string json = JsonConvert.SerializeObject(results, Formatting.Indented);
            File.WriteAllText(outputPath, json);
            Console.WriteLine($"Wrote {results.Count} simulation results to {outputPath}");
        }

        private static List<(string Name, SimulationSettings Settings)> CreateCommonScenarios(
            SimulationSettings baseline)
        {
            List<(string Name, SimulationSettings Settings)> scenarios =
                new() { ("baseline", baseline) };

            void Add(string name, Func<SimulationSettings, SimulationSettings> vary)
            {
                scenarios.Add((name, vary(baseline)));
            }

            Add("replicas-low", settings => settings with { NumReplicas = 5 });
            Add("replicas-high", settings => settings with { NumReplicas = 20 });
            Add("network-delay-low", settings => settings with { MinDelay = 0.001f, MaxDelay = 0.01f });
            Add("network-delay-high", settings => settings with { MinDelay = 0.05f, MaxDelay = 0.5f });
            Add("duration-low", settings => settings with { MaxDuration = 20.0f });
            Add("duration-high", settings => settings with { MaxDuration = 500.0f });

            Add("operations-low", settings => settings with { OpsPerReplica = 1 });
            Add("operations-high", settings => settings with { OpsPerReplica = 10 });
            Add("send-interval-low", settings => settings with { DelayBetweenSends = 0.1f });
            Add("send-interval-high", settings => settings with { DelayBetweenSends = 0.8f });
            Add("operation-interval-low", settings => settings with { DelayBetweenOps = 0.025f });
            Add("operation-interval-high", settings => settings with { DelayBetweenOps = 0.2f });
            Add("heartbeat-low", settings => settings with { HeartbeatTime = 0.05f });
            Add("heartbeat-high", settings => settings with { HeartbeatTime = 0.5f });
            Add("timeout-low", settings => settings with { TimeoutTime = 2.0f });
            Add("timeout-high", settings => settings with { TimeoutTime = 30.0f });
            Add("hop-limit-low", settings => settings with { NumHops = 2 });
            Add("hop-limit-high", settings => settings with { NumHops = 20 });
            Add("forgetting-time-low", settings => settings with { ForgettingTime = 10.0f });
            Add("forgetting-time-high", settings => settings with { ForgettingTime = 120.0f });
            Add("ack-timeout-low", settings => settings with { AckTimeout = 1.0f });
            Add("ack-timeout-high", settings => settings with { AckTimeout = 15.0f });
            Add("distance-vector-poll-low", settings => settings with { DvPollTime = 1.0f });
            Add("distance-vector-poll-high", settings => settings with { DvPollTime = 20.0f });

            return scenarios;
        }

        private static void AddTopologySpecificScenarios(
            string topology,
            SimulationSettings baseline,
            List<(string Name, SimulationSettings Settings)> scenarios)
        {
            void Add(string name, Func<SimulationSettings, SimulationSettings> vary)
            {
                scenarios.Add((name, vary(baseline)));
            }

            if (topology == "random" || topology == "scale-free")
            {
                Add("seed-low", settings => settings with { Seed = 1 });
                Add("seed-high", settings => settings with { Seed = 2026 });
                Add("minimum-delay-low", settings => settings with { MinDelay = 0.001f });
                Add("minimum-delay-high", settings => settings with { MinDelay = 0.05f });
                Add("maximum-delay-low", settings => settings with { MaxDelay = 0.05f });
                Add("maximum-delay-high", settings => settings with { MaxDelay = 0.5f });
            }

            switch (topology)
            {
                case "star":
                    Add("center-first", settings => settings with { CenterNode = 0 });
                    Add("center-last", settings => settings with
                    {
                        CenterNode = settings.NumReplicas - 1
                    });
                    break;
                case "random":
                    Add("edge-probability-low", settings => settings with
                    {
                        ExtraEdgeProbability = 0.0
                    });
                    Add("edge-probability-high", settings => settings with
                    {
                        ExtraEdgeProbability = 0.5
                    });
                    break;
                case "scale-free":
                    Add("edges-per-node-low", settings => settings with { EdgesPerNewNode = 1 });
                    Add("edges-per-node-high", settings => settings with { EdgesPerNewNode = 4 });
                    break;
            }
        }

        private static Experiment CreateExperiment(
            string topology,
            string prefix,
            SimulationSettings settings)
        {
            var insertionParams = settings.CreateInsertionParams();

            return topology switch
            {
                "complete" => new CompleteGraphExperiment(
                    prefix: prefix,
                    numReplicas: settings.NumReplicas,
                    communicationDelay: settings.MinDelay,
                    maxExperimentDuration: settings.MaxDuration,
                    extraParams: insertionParams),
                "ring" => new RingTopologyExperiment(
                    prefix: prefix,
                    numReplicas: settings.NumReplicas,
                    delay: settings.MinDelay,
                    maxExperimentDuration: settings.MaxDuration,
                    runHopelessCases: true,
                    extraParams: insertionParams),
                "star" => new StarTopologyExperiment(
                    prefix: prefix,
                    numReplicas: settings.NumReplicas,
                    centerNode: settings.CenterNode,
                    delay: settings.MinDelay,
                    maxExperimentDuration: settings.MaxDuration,
                    runHopelessCases: true,
                    extraParams: insertionParams),
                "random" => new RandomFullyConnectedExperiment(
                    prefix: prefix,
                    numReplicas: settings.NumReplicas,
                    minDelay: settings.MinDelay,
                    maxDelay: settings.MaxDelay,
                    maxExperimentDuration: settings.MaxDuration,
                    seed: settings.Seed,
                    extraEdgeProbability: settings.ExtraEdgeProbability,
                    runHopelessCases: true,
                    extraParams: insertionParams),
                "scale-free" => new ScaleFreeNetworkExperiment(
                    prefix: prefix,
                    numReplicas: settings.NumReplicas,
                    minDelay: settings.MinDelay,
                    maxDelay: settings.MaxDelay,
                    maxExperimentDuration: settings.MaxDuration,
                    seed: settings.Seed,
                    edgesPerNewNode: settings.EdgesPerNewNode,
                    runHopelessCases: true,
                    extraParams: insertionParams),
                _ => throw new ArgumentOutOfRangeException(nameof(topology), topology, "Unknown topology.")
            };
        }

        private sealed record SimulationSettings(
            int NumReplicas = 10,
            float MinDelay = 0.01f,
            float MaxDelay = 0.2f,
            float MaxDuration = 200.0f,
            int Seed = 42,
            int CenterNode = 0,
            int EdgesPerNewNode = 2,
            double ExtraEdgeProbability = 0.15,
            int OpsPerReplica = 5,
            float DelayBetweenSends = 0.4f,
            float DelayBetweenOps = 0.1f,
            float HeartbeatTime = 0.1f,
            float TimeoutTime = 15.0f,
            int NumHops = 10,
            float ForgettingTime = 60.0f,
            float AckTimeout = 5.0f,
            float DvPollTime = 10.0f)
        {
            public Experiment.InsertionExperimentParams CreateInsertionParams()
            {
                return new Experiment.InsertionExperimentParams(
                    opsPerReplica: OpsPerReplica,
                    delayBetweenSends: DelayBetweenSends,
                    delayBetweenOps: DelayBetweenOps,
                    heartbeatTime: HeartbeatTime,
                    timeoutTime: TimeoutTime,
                    numHops: NumHops,
                    forgetingTime: ForgettingTime,
                    ackTimeout: AckTimeout,
                    dvPollTime: DvPollTime);
            }
        }
    }
}
