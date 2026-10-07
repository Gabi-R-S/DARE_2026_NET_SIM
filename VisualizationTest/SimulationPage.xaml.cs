using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CRDTApp;
using CRDTApp.Behaviours;
using CRDTApp.Messages;
using CRDTApp.NetworkLayers;
using CRDTApp.NetworkLayers.Base;
using CRDTApp.NetworkLayers.Composite;
using CRDTApp.NetworkLayers.Composite.Routing;
using CRDTApp.Schedulers;
using RunSimulations;
using Shared;
using Simulator;
using VisualizationTest.Graph;

namespace VisualizationTest
{
    public partial class SimulationPage : Page
    {
        private SimulationConfig _config;
        private readonly Random _random = new();
        private readonly DispatcherTimer _uiUpdateTimer;

        private RealtimeSimulator? _simulator;
        private readonly List<CRDTApp.App> _apps = new();
        private readonly List<InsertBehaviour> _behaviours = new();
        private readonly Dictionary<ReplicaID, CRDTApp.App> _appsByReplica = new();
        private readonly HashSet<ReplicaID> _completedReplicas = new();
        private GraphState? _graphState;
        private DateTime _simulationStartTime;

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

        public SimulationPage(SimulationConfig? config = null)
        {
            InitializeComponent();

            _config = config?.Clone() ?? SimulationConfig.Current.Clone();

            // Timer for UI stats updates (FPS, counters, convergence, elapsed time)
            _uiUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _uiUpdateTimer.Tick += (_, _) => UpdateStats();

            Loaded += OnPageLoaded;
            Unloaded += OnPageUnloaded;
        }

        // Backward-compatible constructor
        public SimulationPage(int numberOfNodes, float minimumMessageDelay, float maximumMessageDelay, string initialProtocol = "Flood Routing")
            : this(new SimulationConfig
            {
                NumberOfNodes = numberOfNodes,
                MinimumMessageDelay = minimumMessageDelay,
                MaximumMessageDelay = maximumMessageDelay,
                RoutingProtocol = string.IsNullOrWhiteSpace(initialProtocol) ? "Flood Routing" : initialProtocol
            })
        {
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            SetComboBoxItem(ProtocolComboBox, _config.RoutingProtocol);
            PopulateDrawerFromConfig(_config);

            StartSimulation(_config);

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
        /// Initializes the network configuration based on SimulationConfig
        /// and hooks the GraphView to the RealtimeSimulator and App events.
        /// </summary>
        private void StartSimulation(SimulationConfig config, bool forceNewSeed = false)
        {
            StopSimulation();

            int seed = config.ResolveSeed(_random, forceNewSeed);
            if (SeedBadgeTextBlock != null)
            {
                SeedBadgeTextBlock.Text = $"Seed: {seed}";
            }

            // Reset statistics counters
            Interlocked.Exchange(ref _totalPacketsScheduled, 0);
            Interlocked.Exchange(ref _totalPacketsDelivered, 0);
            Interlocked.Exchange(ref _appPacketsScheduled, 0);
            Interlocked.Exchange(ref _protocolPacketsScheduled, 0);
            Interlocked.Exchange(ref _convergedCount, 0);

            // Generate the network configuration based on selected topology
            (ConnectionMap map, List<ReplicaID> replicas) = SimulationConfig.CreateNetwork(config, seed);

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

            // Set up each replica node with its network stack and CRDT InsertBehaviour app
            int opsPerReplica = config.OperationsPerReplica;
            int totalExpectedOps = opsPerReplica * replicas.Count;
            float delayBetweenSends = config.DelayBetweenSends;
            float delayBetweenOps = config.DelayBetweenOperations;

            int i = 0;
            foreach (var replica in replicas)
            {
                (NetworkLayer networkManager, IScheduler scheduler, _) = CreateStack(config.RoutingProtocol, _simulator, replica, config);

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
                _appsByReplica[replica] = app;

                _simulator.AddRunEvent(() => app.Run());
                _simulator.AddCloseEvent(() => Task.Run(app.Close));

                i++;
            }

            // Assign state to graph control and start animation
            GraphViewControl.State = _graphState;
            _simulationStartTime = DateTime.Now;

            if (TopologySubtext != null)
            {
                TopologySubtext.Text = $"{config.Topology} (Seed: {seed}) | {replicas.Count} Nodes | Delays: {config.MinimumMessageDelay:F2}s-{config.MaximumMessageDelay:F2}s | {config.RoutingProtocol} | HB: {config.HeartbeatInterval:F2}s | Timeout: {config.MessageTimeout:F2}s | ACK: {config.AckTimeout:F2}s | Ops/Node: {config.OperationsPerReplica}";
            }

            // Run the RealtimeSimulator in the background
            Task.Run(async () =>
            {
                try
                {
                    if (_simulator != null)
                    {
                        await _simulator.Run(3600);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Simulation run ended: {ex.Message}");
                }
            });

            UpdateStats();
        }

        private static (NetworkLayer, IScheduler, List<ExtraMetric>) CreateStack(
            string stackType,
            NetworkSimulator simulator,
            ReplicaID id,
            SimulationConfig config)
        {
            switch (stackType)
            {
                case "Distance Vector":
                    return NetworkCreationFunctions.CreateBasicDistanceVectorStack(
                        simulator,
                        id,
                        config.MessageTimeout,
                        config.HeartbeatInterval,
                        config.AckTimeout);

                case "Link State":
                    return NetworkCreationFunctions.CreateBasicLinkStateStack(
                        simulator,
                        id,
                        config.MessageTimeout,
                        config.HeartbeatInterval,
                        config.AckTimeout);

                case "No-Route (Ack)":
                    return NetworkCreationFunctions.CreateBasicNoRouteStack(simulator, id, config.AckTimeout);

                case "Base Layer Only":
                    return NetworkCreationFunctions.CreateOnlyBaseLayer(simulator, id);

                case "Flood Routing":
                default:
                    return NetworkCreationFunctions.CreateBasicFloodRoutingStack(
                        simulator,
                        id,
                        config.MessageTimeout,
                        config.HeartbeatInterval,
                        config.MaxHops,
                        ackTimeout: config.AckTimeout);
            }
        }

        private void StopSimulation()
        {
            lock (_completedReplicas)
            {
                _completedReplicas.Clear();
            }
            _appsByReplica.Clear();

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

        private void OnMessageSendScheduled(NetworkMessage message)
        {
            if (_graphState == null || _simulator == null) return;

            var packetInfo = PacketClassifier.Classify(message.Payload);

            var connection = _simulator.ConnectionMap.GetConnection(message.Source, message.Recipient);
            if (connection == null || message.Source == message.Recipient)
            {
                // No physical connection exists between these nodes (e.g. invalid direct send attempt)
                // or self-addressed loopback message; do not animate a particle through thin air.
                return;
            }

            double duration = connection.TransmissionTime > 0
                ? connection.TransmissionTime
                : 0.2;

            _graphState.SpawnParticle(
                message.Source.ID,
                message.Recipient.ID,
                duration,
                packetInfo.Brush,
                packetInfo.GlowBrush,
                packetInfo.Label,
                message,
                packetInfo.Size);

            _graphState.LightUpEdge(message.Source.ID, message.Recipient.ID);

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

        private void OnMessageDelivered(NetworkMessage message)
        {
            Interlocked.Increment(ref _totalPacketsDelivered);
            _graphState?.PulseNode(message.Recipient.ID);
        }

        private void OnOperationApplied(ReplicaID replica, int currentCount, int expectedCount)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (_graphState != null && _graphState.TryGetNode(replica.ID, out var node) && node != null)
                {
                    bool isCompleted;
                    lock (_completedReplicas)
                    {
                        isCompleted = _completedReplicas.Contains(replica);
                    }

                    if (!isCompleted)
                    {
                        double pct = expectedCount > 0 ? (double)currentCount / expectedCount : 0.0;
                        node.Tag = $"Replica {replica.ID}\nCRDT Ops: {currentCount} / {expectedCount} ({pct:P0})\nStatus: Merging operations";
                    }
                }
            }));
        }

        private void OnBehaviourEnded(ReplicaID replica)
        {
            bool isNewlyCompleted = false;
            bool allCompleted = false;

            lock (_completedReplicas)
            {
                if (_completedReplicas.Add(replica))
                {
                    isNewlyCompleted = true;
                    Interlocked.Increment(ref _convergedCount);

                    // 1. Stop this app's usual behaviour (InsertBehaviour); it will only forward messages now
                    if (_appsByReplica.TryGetValue(replica, out var app))
                    {
                        try
                        {
                            app.Behaviour.Stop();
                        }
                        catch { }
                    }

                    // 2. Check if all tasks are completed
                    if (_completedReplicas.Count >= _apps.Count)
                    {
                        allCompleted = true;
                    }
                }
            }

            if (!isNewlyCompleted) return;

            // If all tasks across all apps are completed, close all apps
            if (allCompleted)
            {
                foreach (var app in _apps)
                {
                    try
                    {
                        app.Close();
                    }
                    catch { }
                }
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                if (_graphState != null && _graphState.TryGetNode(replica.ID, out var node) && node != null)
                {
                    node.Stroke = ConvergedNodeStroke;
                    node.Fill = ConvergedNodeFill;
                    node.Tag = allCompleted
                        ? $"Replica {replica.ID}\nCRDT Ops: Fully Converged!\nStatus: Completed (All Apps Closed)"
                        : $"Replica {replica.ID}\nCRDT Ops: Fully Converged!\nStatus: Completed (Forwarding only)";
                }

                if (allCompleted)
                {
                    foreach (var r in _appsByReplica.Keys)
                    {
                        if (_graphState != null && _graphState.TryGetNode(r.ID, out var n) && n != null)
                        {
                            n.Tag = $"Replica {r.ID}\nCRDT Ops: Fully Converged!\nStatus: Completed (All Apps Closed)";
                        }
                    }
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
            int numNodes = _config.NumberOfNodes;

            StatsTextBlock.Text = $"Packets: {totalPackets} | App: {appPackets} | Protocol: {protocolPackets} | In Flight: {activePackets}";

            double convergedPct = numNodes > 0 ? (double)converged / numNodes * 100.0 : 0;
            ConvergenceTextBlock.Text = converged == numNodes
                ? $"All Replicas Converged! ({converged}/{numNodes})"
                : $"Replicas Converged: {converged}/{numNodes} ({convergedPct:F0}%)";

            if (converged == numNodes)
            {
                ConvergenceTextBlock.Foreground = ConvergedNodeStroke;
            }

            TimeSpan elapsed = DateTime.Now - _simulationStartTime;
            SimTimeTextBlock.Text = $"Time: {elapsed.TotalSeconds:F1}s";
            FpsTextBlock.Text = $"FPS: {GraphViewControl.CurrentFps:F0}";
        }

        #region Toolbar UI Event Handlers

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            _uiUpdateTimer.Stop();
            StopSimulation();
            SimulationConfig.Current = _config.Clone();
            NavigationService?.GoBack();
        }

        private void ProtocolComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
                string protocol = (ProtocolComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Flood Routing";
                _config.RoutingProtocol = protocol;
                SetComboBoxItem(DrawerProtocolComboBox, protocol);
                StartSimulation(_config, forceNewSeed: false);
                GraphViewControl.FitToView();
            }
        }

        private void RestartSimulation_Click(object sender, RoutedEventArgs e)
        {
            StartSimulation(_config, forceNewSeed: false);
            GraphViewControl.FitToView();
        }

        private void NewSeedRestart_Click(object sender, RoutedEventArgs e)
        {
            StartSimulation(_config, forceNewSeed: true);
            if (DrawerSeedTextBox != null)
            {
                DrawerSeedTextBox.Text = _config.ActiveSeed.ToString();
            }
            GraphViewControl.FitToView();
        }

        private void ToggleParameters_Click(object sender, RoutedEventArgs e)
        {
            if (ParametersDrawer.Visibility == Visibility.Visible)
            {
                ParametersDrawer.Visibility = Visibility.Collapsed;
            }
            else
            {
                PopulateDrawerFromConfig(_config);
                ParametersDrawer.Visibility = Visibility.Visible;
            }
            GraphViewControl.FitToView();
        }

        private void CloseDrawer_Click(object sender, RoutedEventArgs e)
        {
            ParametersDrawer.Visibility = Visibility.Collapsed;
            GraphViewControl.FitToView();
        }

        private void InjectAppOp_Click(object sender, RoutedEventArgs e)
        {
            if (_behaviours.Count == 0) return;

            List<InsertBehaviour> activeBehaviours;
            lock (_completedReplicas)
            {
                activeBehaviours = _behaviours.FindAll(b => !_completedReplicas.Contains(b.ReplicaID));
            }

            if (activeBehaviours.Count == 0) return;

            int index = _random.Next(activeBehaviours.Count);
            var targetBehaviour = activeBehaviours[index];
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

        #region Drawer Event Handlers

        private void PopulateDrawerFromConfig(SimulationConfig config)
        {
            DrawerNodesTextBox.Text = config.NumberOfNodes.ToString();
            SetComboBoxItem(DrawerTopologyComboBox, config.Topology);
            DrawerExtraEdgeTextBox.Text = config.ExtraEdgeProbability.ToString("F2", CultureInfo.InvariantCulture);

            DrawerUseRandomSeedCheckBox.IsChecked = config.UseRandomSeed;
            DrawerSeedTextBox.Text = config.ActiveSeed.ToString();
            DrawerSeedTextBox.IsEnabled = !config.UseRandomSeed;

            DrawerMinDelayTextBox.Text = config.MinimumMessageDelay.ToString("F2", CultureInfo.InvariantCulture);
            DrawerMaxDelayTextBox.Text = config.MaximumMessageDelay.ToString("F2", CultureInfo.InvariantCulture);

            SetComboBoxItem(DrawerProtocolComboBox, config.RoutingProtocol);
            DrawerHeartbeatTextBox.Text = config.HeartbeatInterval.ToString("F2", CultureInfo.InvariantCulture);
            DrawerTimeoutTextBox.Text = config.MessageTimeout.ToString("F2", CultureInfo.InvariantCulture);
            DrawerAckTimeoutTextBox.Text = config.AckTimeout.ToString("F2", CultureInfo.InvariantCulture);
            DrawerMaxHopsTextBox.Text = config.MaxHops.ToString();

            DrawerOpsTextBox.Text = config.OperationsPerReplica.ToString();
            DrawerDelayOpsTextBox.Text = config.DelayBetweenOperations.ToString("F2", CultureInfo.InvariantCulture);
            DrawerDelaySendsTextBox.Text = config.DelayBetweenSends.ToString("F2", CultureInfo.InvariantCulture);

            UpdateDrawerTopologyControls();
        }

        private void DrawerTopologyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDrawerTopologyControls();
        }

        private void UpdateDrawerTopologyControls()
        {
            if (DrawerExtraEdgeTextBox == null || DrawerExtraEdgeLabel == null) return;
            string selected = (DrawerTopologyComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Random Mesh";
            bool isMesh = selected.Equals("Random Mesh", StringComparison.OrdinalIgnoreCase);
            DrawerExtraEdgeTextBox.IsEnabled = isMesh;
            DrawerExtraEdgeTextBox.Opacity = isMesh ? 1.0 : 0.4;
            DrawerExtraEdgeLabel.Opacity = isMesh ? 1.0 : 0.4;
        }

        private void DrawerUseRandomSeed_Changed(object sender, RoutedEventArgs e)
        {
            if (DrawerSeedTextBox != null)
            {
                DrawerSeedTextBox.IsEnabled = DrawerUseRandomSeedCheckBox.IsChecked != true;
            }
        }

        private void DrawerRandomizeSeed_Click(object sender, RoutedEventArgs e)
        {
            DrawerSeedTextBox.Text = _random.Next(1, 1000000).ToString();
            DrawerUseRandomSeedCheckBox.IsChecked = false;
        }

        private void DrawerApply_Click(object sender, RoutedEventArgs e)
        {
            DrawerValidationTextBlock.Visibility = Visibility.Collapsed;

            // Validate Number of Nodes
            if (!int.TryParse(DrawerNodesTextBox.Text, out int numberOfNodes) || numberOfNodes < 2 || numberOfNodes > 50)
            {
                ShowDrawerValidation("Nodes must be between 2 and 50.");
                return;
            }

            string topology = (DrawerTopologyComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Random Mesh";

            if (!double.TryParse(DrawerExtraEdgeTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double extraEdgeProb) ||
                extraEdgeProb < 0.0 || extraEdgeProb > 1.0)
            {
                ShowDrawerValidation("Extra edge prob must be 0.0 to 1.0.");
                return;
            }

            bool useRandomSeed = DrawerUseRandomSeedCheckBox.IsChecked == true;
            int seed = _config.ActiveSeed;
            if (!useRandomSeed)
            {
                if (!int.TryParse(DrawerSeedTextBox.Text, out seed) || seed <= 0)
                {
                    ShowDrawerValidation("Seed must be a positive integer.");
                    return;
                }
            }

            if (!float.TryParse(DrawerMinDelayTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float minDelay) || minDelay < 0.01f)
            {
                ShowDrawerValidation("Min delay must be ≥ 0.01s.");
                return;
            }

            if (!float.TryParse(DrawerMaxDelayTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float maxDelay) || maxDelay < minDelay)
            {
                ShowDrawerValidation("Max delay must be ≥ Min delay.");
                return;
            }

            string routingProtocol = (DrawerProtocolComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Flood Routing";

            if (!float.TryParse(DrawerHeartbeatTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float heartbeatInterval) || heartbeatInterval < 0.05f)
            {
                ShowDrawerValidation("Heartbeat interval must be ≥ 0.05s.");
                return;
            }

            if (!float.TryParse(DrawerTimeoutTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float messageTimeout) || messageTimeout < 0.1f)
            {
                ShowDrawerValidation("Message timeout must be ≥ 0.1s.");
                return;
            }

            if (!float.TryParse(DrawerAckTimeoutTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float ackTimeout) || ackTimeout < 0.01f)
            {
                ShowDrawerValidation("ACK retry timeout must be ≥ 0.01s.");
                return;
            }

            if (!int.TryParse(DrawerMaxHopsTextBox.Text, out int maxHops) || maxHops < 1)
            {
                ShowDrawerValidation("Max hops must be ≥ 1.");
                return;
            }

            if (!int.TryParse(DrawerOpsTextBox.Text, out int opsPerReplica) || opsPerReplica < 1 || opsPerReplica > 100)
            {
                ShowDrawerValidation("Ops per replica must be 1 to 100.");
                return;
            }

            if (!float.TryParse(DrawerDelayOpsTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float delayBetweenOps) || delayBetweenOps < 0.0f)
            {
                ShowDrawerValidation("Delay between ops cannot be negative.");
                return;
            }

            if (!float.TryParse(DrawerDelaySendsTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float delayBetweenSends) || delayBetweenSends < 0.01f)
            {
                ShowDrawerValidation("Delay between sends must be ≥ 0.01s.");
                return;
            }

            // Update configuration
            _config.NumberOfNodes = numberOfNodes;
            _config.Topology = topology;
            _config.ExtraEdgeProbability = extraEdgeProb;
            _config.UseRandomSeed = useRandomSeed;
            _config.Seed = seed;
            _config.MinimumMessageDelay = minDelay;
            _config.MaximumMessageDelay = maxDelay;
            _config.RoutingProtocol = routingProtocol;
            _config.HeartbeatInterval = heartbeatInterval;
            _config.MessageTimeout = messageTimeout;
            _config.AckTimeout = ackTimeout;
            _config.MaxHops = maxHops;
            _config.OperationsPerReplica = opsPerReplica;
            _config.DelayBetweenOperations = delayBetweenOps;
            _config.DelayBetweenSends = delayBetweenSends;

            SimulationConfig.Current = _config.Clone();

            // Synchronize toolbar protocol dropdown
            SetComboBoxItem(ProtocolComboBox, routingProtocol);

            // Restart simulation with new config
            StartSimulation(_config, forceNewSeed: false);
            GraphViewControl.FitToView();
        }

        private void ShowDrawerValidation(string message)
        {
            DrawerValidationTextBlock.Text = "⚠ " + message;
            DrawerValidationTextBlock.Visibility = Visibility.Visible;
        }

        private static void SetComboBoxItem(ComboBox? comboBox, string value)
        {
            if (comboBox == null) return;
            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }
        }

        #endregion
    }
}