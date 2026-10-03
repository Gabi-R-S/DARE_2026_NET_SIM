using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Shared;
using Simulator;

namespace VisualizationTest.Graph
{
    public static class GraphLayoutHelper
    {
        private static readonly Brush DefaultNodeFill = FreezeBrush(new SolidColorBrush(Color.FromRgb(30, 41, 59))); // Slate 800
        private static readonly Brush DefaultNodeStroke = FreezeBrush(new SolidColorBrush(Color.FromRgb(56, 189, 248))); // Sky 400
        private static readonly Brush DefaultEdgeStroke = FreezeBrush(new SolidColorBrush(Color.FromArgb(160, 100, 116, 139))); // Slate 500

        private static Brush FreezeBrush(Brush b)
        {
            if (b.CanFreeze) b.Freeze();
            return b;
        }

        /// <summary>
        /// Creates a Ring (circular) topology.
        /// </summary>
        public static GraphState CreateRingGraph(
            int count,
            double radius = 220.0,
            Point center = default,
            double minDelay = 0.5,
            double maxDelay = 1.5,
            Random? random = null)
        {
            random ??= new Random();
            var state = new GraphState();

            if (center == default) center = new Point(400, 300);

            for (int i = 0; i < count; i++)
            {
                double angle = 2.0 * Math.PI * i / count - Math.PI / 2.0;
                var pos = new Point(
                    center.X + radius * Math.Cos(angle),
                    center.Y + radius * Math.Sin(angle));

                state.AddNode(new GraphNode(i, pos, $"Node {i}")
                {
                    Fill = DefaultNodeFill,
                    Stroke = DefaultNodeStroke
                });
            }

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                double delay = minDelay + random.NextDouble() * (maxDelay - minDelay);
                state.AddEdge(new GraphEdge(i, next, delay)
                {
                    Stroke = DefaultEdgeStroke,
                    Thickness = 2.0
                });
            }

            return state;
        }

        /// <summary>
        /// Creates a Star topology with node 0 at center and other nodes surrounding it.
        /// </summary>
        public static GraphState CreateStarGraph(
            int count,
            double radius = 220.0,
            Point center = default,
            double minDelay = 0.5,
            double maxDelay = 1.5,
            Random? random = null)
        {
            random ??= new Random();
            var state = new GraphState();

            if (center == default) center = new Point(400, 300);
            if (count < 1) count = 1;

            // Center node
            state.AddNode(new GraphNode(0, center, "Hub 0")
            {
                Radius = 22.0,
                Fill = FreezeBrush(new SolidColorBrush(Color.FromRgb(15, 23, 42))),
                Stroke = FreezeBrush(new SolidColorBrush(Color.FromRgb(245, 158, 11))) // Amber hub
            });

            int outerCount = count - 1;
            for (int i = 1; i < count; i++)
            {
                double angle = 2.0 * Math.PI * (i - 1) / Math.Max(1, outerCount) - Math.PI / 2.0;
                var pos = new Point(
                    center.X + radius * Math.Cos(angle),
                    center.Y + radius * Math.Sin(angle));

                state.AddNode(new GraphNode(i, pos, $"Node {i}")
                {
                    Fill = DefaultNodeFill,
                    Stroke = DefaultNodeStroke
                });

                double delay = minDelay + random.NextDouble() * (maxDelay - minDelay);
                state.AddEdge(new GraphEdge(0, i, delay)
                {
                    Stroke = DefaultEdgeStroke,
                    Thickness = 2.0
                });
            }

            return state;
        }

        /// <summary>
        /// Creates a Mesh / Connected graph with guaranteed connectivity and random cross-links.
        /// </summary>
        public static GraphState CreateMeshGraph(
            int count,
            double radius = 220.0,
            Point center = default,
            double minDelay = 0.5,
            double maxDelay = 1.5,
            double connectivity = 0.35,
            Random? random = null)
        {
            random ??= new Random();
            var state = new GraphState();

            if (center == default) center = new Point(400, 300);

            for (int i = 0; i < count; i++)
            {
                double angle = 2.0 * Math.PI * i / count - Math.PI / 2.0;
                var pos = new Point(
                    center.X + radius * Math.Cos(angle),
                    center.Y + radius * Math.Sin(angle));

                state.AddNode(new GraphNode(i, pos, $"{i}")
                {
                    Fill = DefaultNodeFill,
                    Stroke = DefaultNodeStroke
                });
            }

            var edgeSet = new HashSet<(int, int)>();

            // Guarantee a ring backbone
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                int a = Math.Min(i, next);
                int b = Math.Max(i, next);
                edgeSet.Add((a, b));
            }

            // Add random cross edges based on connectivity
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    if (edgeSet.Contains((i, j))) continue;

                    if (random.NextDouble() < connectivity)
                    {
                        edgeSet.Add((i, j));
                    }
                }
            }

            foreach (var (u, v) in edgeSet)
            {
                double delay = minDelay + random.NextDouble() * (maxDelay - minDelay);
                state.AddEdge(new GraphEdge(u, v, delay)
                {
                    Stroke = DefaultEdgeStroke,
                    Thickness = 1.8
                });
            }

            return state;
        }

        /// <summary>
        /// Creates a 2D Grid / Matrix topology.
        /// </summary>
        public static GraphState CreateGridGraph(
            int count,
            double spacing = 100.0,
            Point center = default,
            double minDelay = 0.5,
            double maxDelay = 1.5,
            Random? random = null)
        {
            random ??= new Random();
            var state = new GraphState();

            if (center == default) center = new Point(400, 300);

            int cols = (int)Math.Ceiling(Math.Sqrt(count));
            int rows = (int)Math.Ceiling((double)count / cols);

            double totalW = (cols - 1) * spacing;
            double totalH = (rows - 1) * spacing;
            double startX = center.X - totalW / 2.0;
            double startY = center.Y - totalH / 2.0;

            for (int i = 0; i < count; i++)
            {
                int r = i / cols;
                int c = i % cols;
                var pos = new Point(startX + c * spacing, startY + r * spacing);

                state.AddNode(new GraphNode(i, pos, $"{i}")
                {
                    Fill = DefaultNodeFill,
                    Stroke = DefaultNodeStroke
                });
            }

            for (int i = 0; i < count; i++)
            {
                int r = i / cols;
                int c = i % cols;

                // Right neighbor
                if (c + 1 < cols && (i + 1) < count)
                {
                    double delay = minDelay + random.NextDouble() * (maxDelay - minDelay);
                    state.AddEdge(new GraphEdge(i, i + 1, delay) { Stroke = DefaultEdgeStroke });
                }

                // Down neighbor
                int down = i + cols;
                if (down < count)
                {
                    double delay = minDelay + random.NextDouble() * (maxDelay - minDelay);
                    state.AddEdge(new GraphEdge(i, down, delay) { Stroke = DefaultEdgeStroke });
                }
            }

            return state;
        }

        /// <summary>
        /// Converts a ConnectionMap from the Simulator into a GraphState.
        /// </summary>
        public static GraphState FromConnectionMap(
            ConnectionMap map,
            double radius = 220.0,
            Point center = default)
        {
            if (center == default) center = new Point(400, 300);
            var state = new GraphState();

            var replicas = new List<ReplicaID>(map.GetAllKnownReplicas());
            replicas.Sort((a, b) => a.ID.CompareTo(b.ID));

            int count = replicas.Count;
            for (int i = 0; i < count; i++)
            {
                double angle = 2.0 * Math.PI * i / Math.Max(1, count) - Math.PI / 2.0;
                var pos = new Point(
                    center.X + radius * Math.Cos(angle),
                    center.Y + radius * Math.Sin(angle));

                state.AddNode(new GraphNode(replicas[i].ID, pos, $"R{replicas[i].ID}")
                {
                    Fill = DefaultNodeFill,
                    Stroke = DefaultNodeStroke
                });
            }

            var visitedPairs = new HashSet<(ulong, ulong)>();
            foreach (var repA in replicas)
            {
                foreach (var repB in replicas)
                {
                    if (repA.ID >= repB.ID) continue;

                    var conn = map.GetConnection(repA, repB);
                    if (conn != null)
                    {
                        visitedPairs.Add((repA.ID, repB.ID));
                        state.AddEdge(new GraphEdge(repA.ID, repB.ID, conn.TransmissionTime)
                        {
                            IsActive = conn.Active,
                            Stroke = DefaultEdgeStroke,
                            Thickness = 2.0,
                            Tag = conn
                        });
                    }
                }
            }

            return state;
        }
    }
}
