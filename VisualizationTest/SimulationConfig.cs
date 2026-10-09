using System;
using System.Collections.Generic;
using RunSimulations;
using Shared;
using Simulator;

namespace VisualizationTest
{
    /// <summary>
    /// Holds all configuration parameters for the mesh network simulation.
    /// </summary>
    public class SimulationConfig
    {
        public static SimulationConfig Current { get; set; } = new();

        // 1. Randomness & Seed
        public bool UseRandomSeed { get; set; } = true;
        public int Seed { get; set; } = 42;
        public int ActiveSeed { get; set; } = 42;

        // 2. Network Scale & Topology
        public int NumberOfNodes { get; set; } = 6;
        public string Topology { get; set; } = "Random Mesh"; // "Random Mesh", "Fully Connected", "Ring", "Star", "Line / Bus"
        public double ExtraEdgeProbability { get; set; } = 0.15;

        // 3. Link Latency
        public float MinimumMessageDelay { get; set; } = 0.5f;
        public float MaximumMessageDelay { get; set; } = 1.5f;

        // 4. Routing & Protocol Timers / Intervals
        public string RoutingProtocol { get; set; } = "Flood Routing";
        public float HeartbeatInterval { get; set; } = 1.0f;
        public float DistanceVectorHeartbeatInterval { get; set; } = 2.0f;
        public float MessageTimeout { get; set; } = 5.0f;
        public float AckTimeout { get; set; } = 5.0f;
        public int MaxHops { get; set; } = 5;

        // 5. Workload / CRDT Operations
        public int OperationsPerReplica { get; set; } = 3;
        public float DelayBetweenOperations { get; set; } = 1.0f;
        public float DelayBetweenSends { get; set; } = 0.5f;

        public SimulationConfig Clone()
        {
            return (SimulationConfig)MemberwiseClone();
        }

        /// <summary>
        /// Resolves the actual seed to use for the next simulation run.
        /// </summary>
        public int ResolveSeed(Random rng, bool forceNewRandom = false)
        {
            if (forceNewRandom || UseRandomSeed)
            {
                ActiveSeed = rng.Next(1, 1000000);
            }
            else
            {
                ActiveSeed = Seed > 0 ? Seed : 42;
            }
            return ActiveSeed;
        }

        /// <summary>
        /// Builds the ConnectionMap and ReplicaIDs based on the selected topology and parameters.
        /// </summary>
        public static (ConnectionMap, List<ReplicaID>) CreateNetwork(SimulationConfig config, int seed)
        {
            var rng = new Random(seed);
            int count = Math.Max(2, config.NumberOfNodes);
            float minDelay = Math.Max(0.01f, config.MinimumMessageDelay);
            float maxDelay = Math.Max(minDelay, config.MaximumMessageDelay);

            float RandomDelay() => minDelay + (float)(rng.NextDouble() * (maxDelay - minDelay));

            switch (config.Topology)
            {
                case "Fully Connected":
                {
                    var map = new ConnectionMap();
                    var replicas = new List<ReplicaID>();
                    for (int i = 0; i < count; i++)
                    {
                        replicas.Add(new ReplicaID((ulong)i));
                    }
                    for (int i = 0; i < count; i++)
                    {
                        for (int j = i + 1; j < count; j++)
                        {
                            map.SetConnection(replicas[i], replicas[j], new NetworkConnection(RandomDelay()));
                        }
                    }
                    return (map, replicas);
                }

                case "Ring":
                {
                    var map = new ConnectionMap();
                    var replicas = new List<ReplicaID>();
                    for (int i = 0; i < count; i++)
                    {
                        replicas.Add(new ReplicaID((ulong)i));
                    }
                    for (int i = 0; i < count; i++)
                    {
                        int next = (i + 1) % count;
                        map.SetConnection(replicas[i], replicas[next], new NetworkConnection(RandomDelay()));
                    }
                    return (map, replicas);
                }

                case "Star":
                {
                    var map = new ConnectionMap();
                    var replicas = new List<ReplicaID>();
                    for (int i = 0; i < count; i++)
                    {
                        replicas.Add(new ReplicaID((ulong)i));
                    }
                    for (int i = 1; i < count; i++)
                    {
                        map.SetConnection(replicas[0], replicas[i], new NetworkConnection(RandomDelay()));
                    }
                    return (map, replicas);
                }

                case "Line / Bus":
                {
                    var map = new ConnectionMap();
                    var replicas = new List<ReplicaID>();
                    for (int i = 0; i < count; i++)
                    {
                        replicas.Add(new ReplicaID((ulong)i));
                    }
                    for (int i = 0; i < count - 1; i++)
                    {
                        map.SetConnection(replicas[i], replicas[i + 1], new NetworkConnection(RandomDelay()));
                    }
                    return (map, replicas);
                }

                case "Random Mesh":
                default:
                {
                    return NetworkCreationFunctions.CreateRandomFullyConnectedNetwork(
                        count,
                        minDelay,
                        maxDelay,
                        seed,
                        config.ExtraEdgeProbability);
                }
            }
        }
    }
}
