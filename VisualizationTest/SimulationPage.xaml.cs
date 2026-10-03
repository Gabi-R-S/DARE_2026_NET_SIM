using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CRDTApp;
using CRDTApp.Behaviours;
using CRDTApp.NetworkLayers;
using CRDTApp.Schedulers;
using RunSimulations;
using Shared;
using Simulator;
using VisualizationTest.Graph;

namespace VisualizationTest
{
    public partial class SimulationPage : Page
    {
        private readonly int _numberOfNodes;
        private readonly float _minimumMessageDelay;
        private readonly float _maximumMessageDelay;

        private readonly Random _random = new();
        private readonly DispatcherTimer _uiUpdateTimer;

        private RealtimeSimulator? _simulator;
        private readonly List<CRDTApp.App> _apps = new();
        private readonly List<InsertBehaviour> _behaviours = new();
        private GraphState? _graphState;
        private DateTime _simulationStartTime;
        private int _currentSeed;

        // Statistics
        private int _totalPacketsScheduled;
        private int _totalPacketsDelivered;
        private int _appPacketsScheduled;
        private int _protocolPacketsScheduled;
        private int _convergedCount;

        // Node Styling Brushes
        private static readonly Brush ConvergedNodeStroke = Freeze(new SolidColorBrush(Color.FromRgb(16, 185, 129))); // Emerald
        private static readonly Brush ConvergedNodeFill = Freeze(new SolidColorBrush(Color.FromRgb(6, 78, 59))); // Dark Emerald

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            if (freezable.CanFreeze) freezable.Freeze();
            return freezable;
        }

        private string _initialProtocol;

        public SimulationPage(int numberOfNodes, float minimumMessageDelay, float maximumMessageDelay, string initialProtocol = "Flood Routing")
        {
            InitializeComponent();

            _numberOfNodes = Math.Max(2, numberOfNodes);
            _minimumMessageDelay = minimumMessageDelay;
            _maximumMessageDelay = maximumMessageDelay;
            _initialProtocol = string.IsNullOrWhiteSpace(initialProtocol) ? "Flood Routing" : initialProtocol;
            _currentSeed = _random.Next(1, 1000000);

            // Timer for UI stats updates (FPS, counters, convergence, elapsed time)
            _uiUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _uiUpdateTimer.Tick += (_, _) => UpdateStats();

            Loaded += OnPageLoaded;
            Unloaded += OnPageUnloaded;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            if (ProtocolComboBox != null)
            {
                foreach (ComboBoxItem item in ProtocolComboBox.Items)
                {
                    string content = item.Content?.ToString() ?? "";
                    if (content.Equals(_initialProtocol, StringComparison.OrdinalIgnoreCase) ||
                        (_initialProtocol.Contains("Flood", StringComparison.OrdinalIgnoreCase) && content.Contains("Flood", StringComparison.OrdinalIgnoreCase)) ||
                        (_initialProtocol.Contains("Distance", StringComparison.OrdinalIgnoreCase) && content.Contains("Distance", StringComparison.OrdinalIgnoreCase)) ||
                        (_initialProtocol.Contains("Link", StringComparison.OrdinalIgnoreCase) && content.Contains("Link", StringComparison.OrdinalIgnoreCase)))
                    {
                        ProtocolComboBox.SelectedItem = item;
                        break;
                    }
                }
            }

            string protocol = (ProtocolComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? _initialProtocol;
            StartSimulation(protocol, _currentSeed);

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                GraphViewControl.FitToView();
            }));

            _uiUpdateTimer.Start();
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            _uiUpdateTimer.Stop();
            StopSimulation();
            GraphViewControl.StopAnimationLoop();
        }

        /// <summary>
        /// Initializes a random network configuration using NetworkCreationFunctions
        /// and hooks the GraphView to the RealtimeSimulator and App events.
        /// </summary>
        private void StartSimulation(string stackType, int? seed = null)
        {
            StopSimulation();

            _currentSeed = seed ?? _random.Next(1, 1000000);

            // Reset statistics counters
            Interlocked.Exchange(ref _totalPacketsScheduled, 0);
            Interlocked.Exchange(ref _totalPacketsDelivered, 0);
            Interlocked.Exchange(ref _appPacketsScheduled, 0);
            Interlocked.Exchange(ref _protocolPacketsScheduled, 0);
            Interlocked.Exchange(ref _convergedCount, 0);

            // Use NetworkCreationFunctions to generate the random network configuration
            // Set reasonable heartbeat time for real-time visualization
            NetworkCreationFunctions.HEARTBEAT_TIME = 1.0f;
            NetworkCreationFunctions.MESSAGE_TIMEOUT = 5.0f;

            (ConnectionMap map, List<ReplicaID> replicas) =
                NetworkCreationFunctions.CreateRandomFullyConnectedNetwork(
                    _numberOfNodes,
                    _minimumMessageDelay,
                    _maximumMessageDelay,
                    _currentSeed);

            // Compute canvas center and radius for circular node layout
            double canvasWidth = Math.Max(600, GraphViewControl.ActualWidth > 0 ? GraphViewControl.ActualWidth : 800);
            double canvasHeight = Math.Max(400, GraphViewControl.ActualHeight > 0 ? GraphViewControl.ActualHeight : 600);
            Point center = new(canvasWidth / 2.0, canvasHeight / 2.0);
            double radius = Math.Min(canvasWidth, canvasHeight) * 0.38;

            // Build initial GraphState representing network nodes and their connections
            _graphState = GraphLayoutHelper.FromConnectionMap(map, radius, center);

            foreach (var node in _graphState.Nodes.Values)
            {
                node.Tag = $"Replica {node.Id}\nStatus: Initializing\nWaiting for operations...";
            }

            // Create the RealtimeSimulator
            _simulator = new RealtimeSimulator(map);

            // Hook simulator events to represent packets passing between nodes
            _simulator.AddMessageSendScheduledHandler(OnMessageSendScheduled);
            _simulator.AddMessageDeliveredHandler(OnMessageDelivered);

            // Select network stack creator function from NetworkCreationFunctions
            Func<NetworkSimulator, ReplicaID, (NetworkLayer, IScheduler, List<ExtraMetric>)> stackCreator = stackType switch
            {
                "Distance Vector" => NetworkCreationFunctions.CreateBasicDistanceVectorStack,
                "Link State" => NetworkCreationFunctions.CreateBasicLinkStateStack,
                "No-Route (Ack)" => NetworkCreationFunctions.CreateBasicNoRouteStack,
                "Base Layer Only" => NetworkCreationFunctions.CreateOnlyBaseLayer,
                _ => NetworkCreationFunctions.CreateBasicFloodRoutingStack
            };

            // Set up each replica node with its network stack and CRDT InsertBehaviour app
            int opsPerReplica = 3;
            int totalExpectedOps = opsPerReplica * replicas.Count;
            float delayBetweenSends = 0.5f;
            float delayBetweenOps = 1.0f;

            int i = 0;
            foreach (var replica in replicas)
            {
                (NetworkLayer networkManager, IScheduler scheduler, _) = stackCreator(_simulator, replica);

                var otherReplicas = new List<ReplicaID>(replicas);
                otherReplicas.Remove(replica);

                int minNum = opsPerReplica * i;
                int maxNum = opsPerReplica * (i + 1) - 1;

                var behaviour = new InsertBehaviour(
                    networkManager,
                    scheduler,
                    replica,
                    otherReplicas.ToArray(),
                    minNum,
                    maxNum,
                    totalExpectedOps,
                    delayBetweenSends,
                    delayBetweenOps);

                // Hook App events to update node status and convergence
                behaviour.OperationApplied += OnOperationApplied;
                behaviour.AddBehaviourEndedHandler(() => OnBehaviourEnded(replica));

                var app = new CRDTApp.App(behaviour, scheduler, networkManager);

                _apps.Add(app);
                _behaviours.Add(behaviour);

                _simulator.AddRunEvent(() => app.Run());
                _simulator.AddCloseEvent(() => Task.Run(app.Close));

                i++;
            }

            // Assign state to graph control and start animation
            GraphViewControl.State = _graphState;
            _simulationStartTime = DateTime.Now;

            if (TopologySubtext != null)
            {
                TopologySubtext.Text = $"Random Network (Seed: {_currentSeed}) | {_numberOfNodes} Nodes | Delays: {_minimumMessageDelay:F2}s - {_maximumMessageDelay:F2}s | {stackType}";
            }

            // Run the RealtimeSimulator in the background
            Task.Run(async () =>
            {
                try
                {
                    await _simulator.Run(3600);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Simulation run ended: {ex.Message}");
                }
            });

            UpdateStats();
        }

        private void StopSimulation()
        {
            if (_apps.Count > 0)
            {
                foreach (var app in _apps)
                {
                    try
                    {
                        app.Close();
                    }
                    catch { }
                }
                _apps.Clear();
            }

            _behaviours.Clear();
            _simulator = null;
        }

        #region Simulator and App Event Handlers

        /// <summary>
        /// Hooked to Simulator.MessageSendScheduled:
        /// Spawns a particle representing the packet traveling between source and recipient nodes,
        /// using distinct colors for protocol packets vs app-relevant packets.
        /// </summary>
        private void OnMessageSendScheduled(NetworkMessage message)
        {
            if (_graphState == null || _simulator == null) return;

            // Classify packet: protocol packets vs app-relevant packets
            var packetInfo = PacketClassifier.Classify(message.Payload);

            // Retrieve the connection latency to determine flight duration
            var connection = _simulator.ConnectionMap.GetConnection(message.Source, message.Recipient);
            double duration = connection != null && connection.TransmissionTime > 0
                ? connection.TransmissionTime
                : 0.2;

            // Spawn particle traveling between nodes
            _graphState.SpawnParticle(
                message.Source.ID,
                message.Recipient.ID,
                duration,
                packetInfo.Brush,
                packetInfo.GlowBrush,
                packetInfo.Label,
                message);

            // Light up the edge connection to show activity
            _graphState.LightUpEdge(message.Source.ID, message.Recipient.ID);

            // Update stats
            Interlocked.Increment(ref _totalPacketsScheduled);
            if (packetInfo.IsProtocol)
            {
                Interlocked.Increment(ref _protocolPacketsScheduled);
            }
            else
            {
                Interlocked.Increment(ref _appPacketsScheduled);
            }
        }

        /// <summary>
        /// Hooked to Simulator.MessageDelivered:
        /// Signals packet arrival at the destination node.
        /// </summary>
        private void OnMessageDelivered(NetworkMessage message)
        {
            Interlocked.Increment(ref _totalPacketsDelivered);
            _graphState?.PulseNode(message.Recipient.ID);
        }

        /// <summary>
        /// Hooked to App.InsertBehaviour.OperationApplied:
        /// Updates the node's visual info with CRDT convergence progress.
        /// </summary>
        private void OnOperationApplied(ReplicaID replica, int currentCount, int expectedCount)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (_graphState != null && _graphState.TryGetNode(replica.ID, out var node) && node != null)
                {
                    double pct = expectedCount > 0 ? (double)currentCount / expectedCount : 0.0;
                    node.Tag = $"Replica {replica.ID}\nCRDT Ops: {currentCount} / {expectedCount} ({pct:P0})\nStatus: Merging operations";
                }
            }));
        }

        /// <summary>
        /// Hooked to App.InsertBehaviour.BehaviourEnded:
        /// Highlights the replica node when it has successfully converged.
        /// </summary>
        private void OnBehaviourEnded(ReplicaID replica)
        {
            Interlocked.Increment(ref _convergedCount);

            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                if (_graphState != null && _graphState.TryGetNode(replica.ID, out var node) && node != null)
                {
                    node.Stroke = ConvergedNodeStroke;
                    node.Fill = ConvergedNodeFill;
                    node.Tag = $"Replica {replica.ID}\nCRDT Ops: Fully Converged!\nStatus: Completed";
                }
            }));
        }

        #endregion

        private void UpdateStats()
        {
            var state = GraphViewControl.State;
            if (state == null) return;

            int totalPackets = _totalPacketsScheduled;
            int appPackets = _appPacketsScheduled;
            int protocolPackets = _protocolPacketsScheduled;
            int activePackets = state.ParticleCount;
            int converged = _convergedCount;

            StatsTextBlock.Text = $"Packets: {totalPackets} | App: {appPackets} | Protocol: {protocolPackets} | In Flight: {activePackets}";

            double convergedPct = _numberOfNodes > 0 ? (double)converged / _numberOfNodes * 100.0 : 0;
            ConvergenceTextBlock.Text = converged == _numberOfNodes
                ? $"All Replicas Converged! ({converged}/{_numberOfNodes})"
                : $"Replicas Converged: {converged}/{_numberOfNodes} ({convergedPct:F0}%)";

            if (converged == _numberOfNodes)
            {
                ConvergenceTextBlock.Foreground = ConvergedNodeStroke;
            }

            TimeSpan elapsed = DateTime.Now - _simulationStartTime;
            SimTimeTextBlock.Text = $"Time: {elapsed.TotalSeconds:F1}s";
            FpsTextBlock.Text = $"FPS: {GraphViewControl.CurrentFps:F0}";
        }

        #region UI Event Handlers

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            _uiUpdateTimer.Stop();
            StopSimulation();
            NavigationService?.GoBack();
        }

        private void ProtocolComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
                string protocol = (ProtocolComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Flood Routing";
                StartSimulation(protocol, _currentSeed);
                GraphViewControl.FitToView();
            }
        }

        private void RestartSimulation_Click(object sender, RoutedEventArgs e)
        {
            string protocol = (ProtocolComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Flood Routing";
            StartSimulation(protocol, _random.Next(1, 1000000));
            GraphViewControl.FitToView();
        }

        private void InjectAppOp_Click(object sender, RoutedEventArgs e)
        {
            if (_behaviours.Count == 0) return;

            // Pick a random replica to inject a manual CRDT operation
            int index = _random.Next(_behaviours.Count);
            var targetBehaviour = _behaviours[index];
            int opVal = 1000 + _random.Next(9000);
            targetBehaviour.InsertManualValue(opVal);
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            GraphViewControl.Zoom *= 1.25;
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            GraphViewControl.Zoom *= 0.8;
        }

        private void FitView_Click(object sender, RoutedEventArgs e)
        {
            GraphViewControl.FitToView();
        }

        #endregion
    }
}