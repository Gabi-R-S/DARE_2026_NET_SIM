using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace VisualizationTest.Graph
{
    /// <summary>
    /// Represents a node in the graph visualization.
    /// </summary>
    public sealed class GraphNode
    {
        public object Id { get; init; }
        public Point Position { get; set; }
        public double Radius { get; set; } = 18.0;
        public string? Label { get; set; }

        public Brush? Fill { get; set; }
        public Brush? Stroke { get; set; }
        public double StrokeThickness { get; set; } = 2.0;
        public Brush? TextBrush { get; set; }

        /// <summary>
        /// Optional extra metadata (e.g. replica state, buffer size, status).
        /// </summary>
        public object? Tag { get; set; }

        public GraphNode(object id, Point position, string? label = null)
        {
            Id = id;
            Position = position;
            Label = label ?? id.ToString();
        }
    }

    /// <summary>
    /// Represents a connection between two nodes.
    /// </summary>
    public sealed class GraphEdge
    {
        public object SourceId { get; init; }
        public object TargetId { get; init; }

        /// <summary>
        /// Whether the edge is directed (draws directional arrow).
        /// </summary>
        public bool IsDirected { get; set; } = false;

        /// <summary>
        /// Whether the connection is active/online.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Activity factor from 0.0 (idle) to 1.0 (busy/glow).
        /// </summary>
        public double Activity { get; set; } = 0.0;

        /// <summary>
        /// Latency or transmission time in seconds.
        /// </summary>
        public double TransmissionTime { get; set; } = 0.1;

        public Brush? Stroke { get; set; }
        public double Thickness { get; set; } = 1.5;
        public string? Label { get; set; }

        /// <summary>
        /// Optional metadata (e.g. NetworkConnection instance).
        /// </summary>
        public object? Tag { get; set; }

        public GraphEdge(object sourceId, object targetId, double transmissionTime = 0.1, bool isDirected = false)
        {
            SourceId = sourceId;
            TargetId = targetId;
            TransmissionTime = transmissionTime;
            IsDirected = isDirected;
        }

        public string Key => $"{SourceId}->{TargetId}";
    }

    /// <summary>
    /// Represents an animated packet or signal traveling along an edge.
    /// </summary>
    public sealed class GraphParticle
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public object SourceId { get; init; }
        public object TargetId { get; init; }

        /// <summary>
        /// Progress along the edge from 0.0 (source) to 1.0 (target).
        /// </summary>
        public double Progress { get; set; } = 0.0;

        /// <summary>
        /// Total flight duration in seconds.
        /// </summary>
        public double DurationSeconds { get; set; } = 1.0;

        /// <summary>
        /// Speed of progress advancement per second (1.0 / DurationSeconds).
        /// </summary>
        public double Speed => DurationSeconds > 0 ? 1.0 / DurationSeconds : 10.0;

        /// <summary>
        /// Radius of the particle dot.
        /// </summary>
        public double Size { get; set; } = 5.0;

        /// <summary>
        /// Particle color. If null, a vibrant default theme color is used.
        /// </summary>
        public Brush? Fill { get; set; }

        /// <summary>
        /// Particle glow color.
        /// </summary>
        public Brush? GlowBrush { get; set; }

        /// <summary>
        /// Optional brief label (e.g. "ACK", "MSG", "SYNC").
        /// </summary>
        public string? Label { get; set; }

        /// <summary>
        /// If true, automatically removes this particle when Progress reaches 1.0.
        /// </summary>
        public bool AutoRemoveOnArrival { get; set; } = true;

        /// <summary>
        /// Optional payload associated with this particle (e.g. NetworkMessage).
        /// </summary>
        public object? Payload { get; set; }

        public GraphParticle(object sourceId, object targetId, double durationSeconds = 1.0, Brush? fill = null, Brush? glowBrush = null, string? label = null)
        {
            SourceId = sourceId;
            TargetId = targetId;
            DurationSeconds = Math.Max(0.01, durationSeconds);
            Fill = fill;
            GlowBrush = glowBrush;
            Label = label;
        }
    }

    /// <summary>
    /// Holds the full snapshot of the graph: nodes, edges, and active particles.
    /// Supports thread-safe particle enqueueing from simulation threads.
    /// </summary>
    public sealed class GraphState
    {
        private readonly Dictionary<object, GraphNode> _nodes = new();
        private readonly List<GraphEdge> _edges = new();
        private readonly List<GraphParticle> _particles = new();

        // Lock for thread-safe access between simulation and rendering
        private readonly object _syncLock = new();

        // Queue for background threads to safely submit particles without blocking
        private readonly ConcurrentQueue<GraphParticle> _pendingParticles = new();

        public IReadOnlyDictionary<object, GraphNode> Nodes
        {
            get
            {
                lock (_syncLock) return new Dictionary<object, GraphNode>(_nodes);
            }
        }

        public IReadOnlyList<GraphEdge> Edges
        {
            get
            {
                lock (_syncLock) return new List<GraphEdge>(_edges);
            }
        }

        public IReadOnlyList<GraphParticle> Particles
        {
            get
            {
                lock (_syncLock) return new List<GraphParticle>(_particles);
            }
        }

        public int NodeCount { get { lock (_syncLock) return _nodes.Count; } }
        public int EdgeCount { get { lock (_syncLock) return _edges.Count; } }
        public int ParticleCount { get { lock (_syncLock) return _particles.Count + _pendingParticles.Count; } }

        public void AddNode(GraphNode node)
        {
            lock (_syncLock)
            {
                _nodes[node.Id] = node;
            }
        }

        public void AddEdge(GraphEdge edge)
        {
            lock (_syncLock)
            {
                _edges.Add(edge);
            }
        }

        public bool MoveNode(object id, Point position)
        {
            lock (_syncLock)
            {
                if (!TryGetNode(id, out var node) || node == null) return false;

                node.Position = position;
                return true;
            }
        }

        public bool ToggleEdgeActive(GraphEdge edge)
        {
            lock (_syncLock)
            {
                if (!_edges.Contains(edge)) return false;

                edge.IsActive = !edge.IsActive;
                return true;
            }
        }

        public void Clear()
        {
            lock (_syncLock)
            {
                _nodes.Clear();
                _edges.Clear();
                _particles.Clear();
                while (_pendingParticles.TryDequeue(out _)) { }
            }
        }

        /// <summary>
        /// Thread-safe: can be called from any background simulation thread.
        /// </summary>
        public void SpawnParticle(GraphParticle particle)
        {
            _pendingParticles.Enqueue(particle);
        }

        /// <summary>
        /// Thread-safe convenience method to spawn a particle.
        /// </summary>
        public void SpawnParticle(object sourceId, object targetId, double durationSeconds, Brush? fill = null, Brush? glowBrush = null, string? label = null, object? payload = null, double size = 5.0)
        {
            var p = new GraphParticle(sourceId, targetId, durationSeconds, fill, glowBrush, label)
            {
                Payload = payload,
                Size = size
            };
            SpawnParticle(p);
        }

        public void SpawnParticle(object sourceId, object targetId, double durationSeconds, Brush? fill = null, string? label = null, object? payload = null)
        {
            SpawnParticle(sourceId, targetId, durationSeconds, fill, null, label, payload);
        }

        public void LightUpEdge(object sourceId, object targetId)
        {
            lock (_syncLock)
            {
                ulong s = sourceId is Shared.ReplicaID rs ? rs.ID : Convert.ToUInt64(sourceId);
                ulong t = targetId is Shared.ReplicaID rt ? rt.ID : Convert.ToUInt64(targetId);

                foreach (var edge in _edges)
                {
                    ulong es = edge.SourceId is Shared.ReplicaID ers ? ers.ID : Convert.ToUInt64(edge.SourceId);
                    ulong et = edge.TargetId is Shared.ReplicaID ert ? ert.ID : Convert.ToUInt64(edge.TargetId);

                    if ((es == s && et == t) || (!edge.IsDirected && es == t && et == s))
                    {
                        edge.Activity = 1.0;
                        break;
                    }
                }
            }
        }

        public void PulseNode(object nodeId)
        {
            lock (_syncLock)
            {
                if (TryGetNode(nodeId, out var node) && node != null)
                {
                    // Tag node or stroke pulse if needed
                }
            }
        }

        /// <summary>
        /// Internal method called on UI render thread to advance particles by deltaTime.
        /// </summary>
        internal void AdvanceParticles(double deltaTimeSeconds, Action<GraphParticle>? onArrived = null)
        {
            // Drain pending particles
            while (_pendingParticles.TryDequeue(out var pending))
            {
                lock (_syncLock)
                {
                    _particles.Add(pending);
                }
            }

            lock (_syncLock)
            {
                for (int i = _particles.Count - 1; i >= 0; i--)
                {
                    var p = _particles[i];
                    p.Progress += p.Speed * deltaTimeSeconds;

                    if (p.Progress >= 1.0)
                    {
                        p.Progress = 1.0;
                        onArrived?.Invoke(p);

                        if (p.AutoRemoveOnArrival)
                        {
                            _particles.RemoveAt(i);
                        }
                    }
                }

                // Decay edge glowing activity
                foreach (var edge in _edges)
                {
                    if (edge.Activity > 0)
                    {
                        edge.Activity = Math.Max(0.0, edge.Activity - deltaTimeSeconds * 1.5);
                    }
                }
            }
        }

        /// <summary>
        /// Finds node by Id with internal lock, safely matching ulong or ReplicaID.
        /// </summary>
        public bool TryGetNode(object id, out GraphNode? node)
        {
            lock (_syncLock)
            {
                if (_nodes.TryGetValue(id, out node)) return true;
                if (id is Shared.ReplicaID rep && _nodes.TryGetValue(rep.ID, out node)) return true;
                if (id is ulong uid && _nodes.TryGetValue(new Shared.ReplicaID(uid), out node)) return true;
                if (ulong.TryParse(id?.ToString(), out ulong parsed))
                {
                    if (_nodes.TryGetValue(parsed, out node)) return true;
                    if (_nodes.TryGetValue(new Shared.ReplicaID(parsed), out node)) return true;
                }
                node = null;
                return false;
            }
        }

        /// <summary>
        /// Executes a batch drawing operation safely under synchronization lock.
        /// </summary>
        public void ForEachItem(
            Action<IEnumerable<GraphEdge>> drawEdges,
            Action<IEnumerable<GraphParticle>> drawParticles,
            Action<IEnumerable<GraphNode>> drawNodes)
        {
            lock (_syncLock)
            {
                drawEdges(_edges);
                drawParticles(_particles);
                drawNodes(_nodes.Values);
            }
        }
    }
}
