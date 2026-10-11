using Newtonsoft.Json;
using System.Globalization;

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
            List<ExperimentResults> results = new();

            foreach (string topology in Topologies)
            {
                var scenarios = new List<(string Name, SimulationSettings Settings)>
                {
                    ("baseline", baseline)
                };
                scenarios.AddRange(CreateFactorialScenarios(topology, baseline));
                scenarios.AddRange(CreateParameterScenarios(topology, baseline));

                foreach (var (scenarioName, settings) in scenarios)
                {
                    Experiment experiment = CreateExperiment(
                        topology,
                        $"{scenarioName}-",
                        ApplyTimeoutBounds(topology, settings));
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

        private static List<(string Name, SimulationSettings Settings)> CreateFactorialScenarios(
            string topology,
            SimulationSettings baseline)
        {
            int[] replicaCounts = { 5, 8, 10, 12, 15, 18, 20 };
            (float MinDelay, float MaxDelay)[] delayProfiles =
                topology is "random" or "scale-free"
                    ? new[]
                    {
                        (0.001f, 0.01f),
                        (0.0025f, 0.025f),
                        (0.005f, 0.05f),
                        (0.01f, 0.2f),
                        (0.02f, 0.2f),
                        (0.025f, 0.25f),
                        (0.05f, 0.5f),
                        (0.1f, 1.0f)
                    }
                    : new[]
                    {
                        (0.001f, 0.001f),
                        (0.0025f, 0.0025f),
                        (0.005f, 0.005f),
                        (0.01f, 0.01f),
                        (0.02f, 0.02f),
                        (0.025f, 0.025f),
                        (0.05f, 0.05f),
                        (0.1f, 0.1f)
                    };
            double[] edgeProbabilities = topology == "random"
                ? new[] { 0.0, 0.05, 0.1, 0.15, 0.2, 0.25, 0.3, 0.4, 0.5 }
                : new[] { baseline.ExtraEdgeProbability };
            List<(string Name, SimulationSettings Settings)> scenarios = new();

            foreach (int numReplicas in replicaCounts)
            {
                foreach (var (minDelay, maxDelay) in delayProfiles)
                {
                    foreach (double probability in edgeProbabilities)
                    {
                        string scenarioName =
                            $"factorial-nodes-{numReplicas}-delay-{FormatScenarioValue(minDelay)}-{FormatScenarioValue(maxDelay)}";
                        if (topology == "random")
                        {
                            scenarioName += $"-edge-{FormatScenarioValue(probability)}";
                        }

                        scenarios.Add((scenarioName, baseline with
                        {
                            NumReplicas = numReplicas,
                            MinDelay = minDelay,
                            MaxDelay = maxDelay,
                            ExtraEdgeProbability = probability
                        }));
                    }
                }
            }

            return scenarios;
        }

        private static List<(string Name, SimulationSettings Settings)> CreateParameterScenarios(
            string topology,
            SimulationSettings baseline)
        {
            List<(string Name, SimulationSettings Settings)> scenarios = new();

            void Add(string name, Func<SimulationSettings, SimulationSettings> vary) =>
                scenarios.Add((name, vary(baseline)));

            Add("duration-low", settings => settings with { MaxDuration = 20.0f });
            Add("duration-medium", settings => settings with { MaxDuration = 100.0f });
            Add("duration-high", settings => settings with { MaxDuration = 500.0f });
            Add("operations-low", settings => settings with { OpsPerReplica = 1 });
            Add("operations-medium", settings => settings with { OpsPerReplica = 6 });
            Add("operations-high", settings => settings with { OpsPerReplica = 10 });
            Add("send-interval-low", settings => settings with { DelayBetweenSends = 0.1f });
            Add("send-interval-medium", settings => settings with { DelayBetweenSends = 0.45f });
            Add("send-interval-high", settings => settings with { DelayBetweenSends = 0.8f });
            Add("operation-interval-low", settings => settings with { DelayBetweenOps = 0.025f });
            Add("operation-interval-medium", settings => settings with { DelayBetweenOps = 0.125f });
            Add("operation-interval-high", settings => settings with { DelayBetweenOps = 0.2f });
            Add("heartbeat-low", settings => settings with { HeartbeatTime = 0.05f });
            Add("heartbeat-medium", settings => settings with { HeartbeatTime = 0.2f });
            Add("heartbeat-high", settings => settings with { HeartbeatTime = 0.5f });
            Add("timeout-low", settings => settings with { TimeoutTime = 2.0f });
            Add("timeout-medium", settings => settings with { TimeoutTime = 16.0f });
            Add("timeout-high", settings => settings with { TimeoutTime = 30.0f });
            Add("hop-limit-low", settings => settings with { NumHops = 2 });
            Add("hop-limit-medium", settings => settings with { NumHops = 11 });
            Add("hop-limit-high", settings => settings with { NumHops = 20 });
            Add("forgetting-time-low", settings => settings with { ForgettingTime = 10.0f });
            Add("forgetting-time-medium", settings => settings with { ForgettingTime = 65.0f });
            Add("forgetting-time-high", settings => settings with { ForgettingTime = 120.0f });
            Add("distance-vector-poll-low", settings => settings with { DvPollTime = 1.0f });
            Add("distance-vector-poll-medium", settings => settings with { DvPollTime = 12.0f });
            Add("distance-vector-poll-high", settings => settings with { DvPollTime = 20.0f });

            float baselineAckTimeout = ApplyTimeoutBounds(topology, baseline).AckTimeout;
            foreach (float ackTimeout in new[] { 1.0f, 8.0f, 15.0f })
            {
                var settings = ApplyTimeoutBounds(
                    topology,
                    baseline with { AckTimeout = ackTimeout });
                if (settings.AckTimeout > baselineAckTimeout)
                {
                    Add($"ack-timeout-{FormatScenarioValue(settings.AckTimeout)}",
                        _ => settings);
                }
            }

            if (topology is "random" or "scale-free")
            {
                Add("seed-low", settings => settings with { Seed = 1 });
                Add("seed-high", settings => settings with { Seed = 2026 });
            }

            if (topology == "star")
            {
                Add("center-first", settings => settings with { CenterNode = 0 });
                Add("center-last", settings => settings with
                {
                    CenterNode = settings.NumReplicas - 1
                });
            }
            else if (topology == "scale-free")
            {
                Add("edges-per-node-low", settings => settings with { EdgesPerNewNode = 1 });
                Add("edges-per-node-medium", settings => settings with { EdgesPerNewNode = 3 });
                Add("edges-per-node-high", settings => settings with { EdgesPerNewNode = 4 });
            }

            return scenarios;
        }

        private static SimulationSettings ApplyTimeoutBounds(
            string topology,
            SimulationSettings settings)
        {
            float maximumOneWayDelay =
                topology is "random" or "scale-free"
                    ? settings.MaxDelay
                    : settings.MinDelay;
            float heartbeatTimeoutFloor =
                (2 * maximumOneWayDelay) + (3 * settings.HeartbeatTime);
            // Allow 1.25x the worst-case round trip across a simple path of N-1 links.
            float acknowledgementTimeoutFloor =
                2.5f * (settings.NumReplicas - 1) * maximumOneWayDelay;

            return settings with
            {
                TimeoutTime = Math.Max(settings.TimeoutTime, heartbeatTimeoutFloor),
                AckTimeout = Math.Max(settings.AckTimeout, acknowledgementTimeoutFloor)
            };
        }

        private static string FormatScenarioValue(double value) =>
            value.ToString("0.####", CultureInfo.InvariantCulture);

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
