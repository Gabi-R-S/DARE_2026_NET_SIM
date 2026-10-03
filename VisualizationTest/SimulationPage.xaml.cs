using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VisualizationTest.Graph;

namespace VisualizationTest
{
    public partial class SimulationPage : Page
    {
        private readonly int _numberOfNodes;
        private readonly float _minimumMessageDelay;
        private readonly float _maximumMessageDelay;

        private readonly Random _random = new();
        private readonly DispatcherTimer _trafficTimer;
        private readonly DispatcherTimer _uiUpdateTimer;

        private static readonly Brush[] ParticlePalette =
        {
            Freeze(new SolidColorBrush(Color.FromRgb(245, 158, 11))),  // Amber
            Freeze(new SolidColorBrush(Color.FromRgb(16, 185, 129))),  // Emerald
            Freeze(new SolidColorBrush(Color.FromRgb(56, 189, 248))),  // Sky
            Freeze(new SolidColorBrush(Color.FromRgb(236, 72, 153))),  // Pink
            Freeze(new SolidColorBrush(Color.FromRgb(168, 85, 247))),  // Purple
            Freeze(new SolidColorBrush(Color.FromRgb(234, 179, 8)))    // Yellow
        };

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            if (freezable.CanFreeze) freezable.Freeze();
            return freezable;
        }

        public SimulationPage(int numberOfNodes, float minimumMessageDelay, float maximumMessageDelay)
        {
            InitializeComponent();

            _numberOfNodes = Math.Max(2, numberOfNodes);
            _minimumMessageDelay = minimumMessageDelay;
            _maximumMessageDelay = maximumMessageDelay;

            // Timer for auto traffic generation
            _trafficTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            _trafficTimer.Tick += (_, _) => SpawnRandomPacket();

            // Timer for UI stats updates (FPS, active packets)
            _uiUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _uiUpdateTimer.Tick += (_, _) => UpdateStats();

            Loaded += OnPageLoaded;
            Unloaded += OnPageUnloaded;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            RebuildTopology();

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                GraphViewControl.FitToView();
            }));

            _uiUpdateTimer.Start();
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            _trafficTimer.Stop();
            _uiUpdateTimer.Stop();
            GraphViewControl.StopAnimationLoop();
        }

        private void RebuildTopology()
        {
            string topology = (TopologyComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Mesh";

            double canvasWidth = Math.Max(600, GraphViewControl.ActualWidth > 0 ? GraphViewControl.ActualWidth : 800);
            double canvasHeight = Math.Max(400, GraphViewControl.ActualHeight > 0 ? GraphViewControl.ActualHeight : 600);
            Point center = new(canvasWidth / 2.0, canvasHeight / 2.0);
            double radius = Math.Min(canvasWidth, canvasHeight) * 0.38;

            GraphState state = topology switch
            {
                "Ring" => GraphLayoutHelper.CreateRingGraph(
                    _numberOfNodes,
                    radius,
                    center,
                    _minimumMessageDelay,
                    _maximumMessageDelay,
                    _random),

                "Star" => GraphLayoutHelper.CreateStarGraph(
                    _numberOfNodes,
                    radius,
                    center,
                    _minimumMessageDelay,
                    _maximumMessageDelay,
                    _random),

                "Grid" => GraphLayoutHelper.CreateGridGraph(
                    _numberOfNodes,
                    Math.Max(60.0, 500.0 / Math.Sqrt(_numberOfNodes)),
                    center,
                    _minimumMessageDelay,
                    _maximumMessageDelay,
                    _random),

                _ => GraphLayoutHelper.CreateMeshGraph(
                    _numberOfNodes,
                    radius,
                    center,
                    _minimumMessageDelay,
                    _maximumMessageDelay,
                    connectivity: 0.35,
                    _random)
            };

            GraphViewControl.State = state;

            if (TopologySubtext != null)
            {
                TopologySubtext.Text = $"{topology} Topology | {_numberOfNodes} Nodes | Delays: {_minimumMessageDelay:F2}s - {_maximumMessageDelay:F2}s";
            }

            UpdateStats();
        }

        private void SpawnRandomPacket()
        {
            var state = GraphViewControl.State;
            if (state == null || state.EdgeCount == 0) return;

            var edges = state.Edges;
            if (edges.Count == 0) return;

            // Pick a random edge
            var edge = edges[_random.Next(edges.Count)];

            // Random direction along the edge
            object src = edge.SourceId;
            object tgt = edge.TargetId;
            if (_random.Next(2) == 1 && !edge.IsDirected)
            {
                (src, tgt) = (tgt, src);
            }

            // Duration based on edge or configured delay range
            double duration = edge.TransmissionTime > 0
                ? edge.TransmissionTime
                : _minimumMessageDelay + _random.NextDouble() * (_maximumMessageDelay - _minimumMessageDelay);

            var color = ParticlePalette[_random.Next(ParticlePalette.Length)];
            state.SpawnParticle(src, tgt, duration, color);
        }

        private void UpdateStats()
        {
            var state = GraphViewControl.State;
            if (state == null) return;

            StatsTextBlock.Text = $"Nodes: {state.NodeCount} | Edges: {state.EdgeCount} | Active Packets: {state.ParticleCount}";
            FpsTextBlock.Text = $"FPS: {GraphViewControl.CurrentFps:F0}";
        }

        #region Event Handlers

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            _trafficTimer.Stop();
            NavigationService?.GoBack();
        }

        private void TopologyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
                RebuildTopology();
                GraphViewControl.FitToView();
            }
        }

        private void SendPacket_Click(object sender, RoutedEventArgs e)
        {
            SpawnRandomPacket();
        }

        private void BurstPacket_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 12; i++)
            {
                SpawnRandomPacket();
            }
        }

        private void AutoTrafficToggle_Checked(object sender, RoutedEventArgs e)
        {
            _trafficTimer.Start();
        }

        private void AutoTrafficToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _trafficTimer.Stop();
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