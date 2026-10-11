using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace VisualizationTest
{
    public partial class InputPage : Page
    {
        private readonly Random _random = new();

        public InputPage()
        {
            InitializeComponent();
            LoadFromConfig(SimulationConfig.Current);
        }

        private void LoadFromConfig(SimulationConfig config)
        {
            NumberOfNodesTextBox.Text = config.NumberOfNodes.ToString();
            SetComboBoxItem(TopologyComboBox, config.Topology);
            ExtraEdgeProbabilityTextBox.Text = config.ExtraEdgeProbability.ToString("F2", CultureInfo.InvariantCulture);

            UseRandomSeedCheckBox.IsChecked = config.UseRandomSeed;
            SeedTextBox.Text = config.Seed.ToString();
            SeedTextBox.IsEnabled = !config.UseRandomSeed;

            MinimumMessageDelayTextBox.Text = config.MinimumMessageDelay.ToString("F2", CultureInfo.InvariantCulture);
            MaximumMessageDelayTextBox.Text = config.MaximumMessageDelay.ToString("F2", CultureInfo.InvariantCulture);

            SetComboBoxItem(RoutingProtocolComboBox, config.RoutingProtocol);
            HeartbeatIntervalTextBox.Text = config.HeartbeatInterval.ToString("F2", CultureInfo.InvariantCulture);
            DistanceVectorHeartbeatTextBox.Text = config.DistanceVectorHeartbeatInterval.ToString("F2", CultureInfo.InvariantCulture);
            MessageTimeoutTextBox.Text = config.MessageTimeout.ToString("F2", CultureInfo.InvariantCulture);
            AckTimeoutTextBox.Text = config.AckTimeout.ToString("F2", CultureInfo.InvariantCulture);
            MaxHopsTextBox.Text = config.MaxHops.ToString();

            OperationsPerReplicaTextBox.Text = config.OperationsPerReplica.ToString();
            DelayBetweenOpsTextBox.Text = config.DelayBetweenOperations.ToString("F2", CultureInfo.InvariantCulture);
            DelayBetweenSendsTextBox.Text = config.DelayBetweenSends.ToString("F2", CultureInfo.InvariantCulture);

            UpdateTopologyDependentControls();
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

        private void UseRandomSeedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (SeedTextBox != null)
            {
                SeedTextBox.IsEnabled = UseRandomSeedCheckBox.IsChecked != true;
            }
        }

        private void RandomizeSeed_Click(object sender, RoutedEventArgs e)
        {
            SeedTextBox.Text = _random.Next(1, 1000000).ToString();
            UseRandomSeedCheckBox.IsChecked = false;
        }

        private void TopologyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateTopologyDependentControls();
        }

        private void UpdateTopologyDependentControls()
        {
            if (ExtraEdgeProbabilityTextBox == null || ExtraEdgeLabel == null || ExtraEdgeHint == null) return;

            string selected = (TopologyComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Random Mesh";
            bool isMesh = selected.Equals("Random Mesh", StringComparison.OrdinalIgnoreCase);

            ExtraEdgeProbabilityTextBox.IsEnabled = isMesh;
            ExtraEdgeProbabilityTextBox.Opacity = isMesh ? 1.0 : 0.4;
            ExtraEdgeLabel.Opacity = isMesh ? 1.0 : 0.4;
            ExtraEdgeHint.Opacity = isMesh ? 1.0 : 0.4;
        }

        #region Quick Presets

        private void PresetBalanced_Click(object sender, RoutedEventArgs e)
        {
            LoadFromConfig(new SimulationConfig());
            ClearValidation();
        }

        private void PresetFast_Click(object sender, RoutedEventArgs e)
        {
            LoadFromConfig(new SimulationConfig
            {
                NumberOfNodes = 5,
                Topology = "Random Mesh",
                ExtraEdgeProbability = 0.25,
                MinimumMessageDelay = 0.15f,
                MaximumMessageDelay = 0.45f,
                RoutingProtocol = "Distance Vector",
                HeartbeatInterval = 0.4f,
                DistanceVectorHeartbeatInterval = 0.8f,
                MessageTimeout = 2.0f,
                MaxHops = 6,
                OperationsPerReplica = 2,
                DelayBetweenOperations = 0.4f,
                DelayBetweenSends = 0.2f
            });
            ClearValidation();
        }

        private void PresetRing_Click(object sender, RoutedEventArgs e)
        {
            LoadFromConfig(new SimulationConfig
            {
                NumberOfNodes = 7,
                Topology = "Ring",
                MinimumMessageDelay = 0.4f,
                MaximumMessageDelay = 0.8f,
                RoutingProtocol = "Flood Routing",
                HeartbeatInterval = 0.8f,
                MessageTimeout = 4.0f,
                MaxHops = 8,
                OperationsPerReplica = 3,
                DelayBetweenOperations = 0.8f,
                DelayBetweenSends = 0.4f
            });
            ClearValidation();
        }

        private void PresetStar_Click(object sender, RoutedEventArgs e)
        {
            LoadFromConfig(new SimulationConfig
            {
                NumberOfNodes = 6,
                Topology = "Star",
                MinimumMessageDelay = 0.3f,
                MaximumMessageDelay = 0.7f,
                RoutingProtocol = "Flood Routing",
                HeartbeatInterval = 0.8f,
                MessageTimeout = 3.5f,
                MaxHops = 4,
                OperationsPerReplica = 3,
                DelayBetweenOperations = 0.6f,
                DelayBetweenSends = 0.3f
            });
            ClearValidation();
        }

        private void PresetHeavy_Click(object sender, RoutedEventArgs e)
        {
            LoadFromConfig(new SimulationConfig
            {
                NumberOfNodes = 8,
                Topology = "Random Mesh",
                ExtraEdgeProbability = 0.3,
                MinimumMessageDelay = 0.4f,
                MaximumMessageDelay = 1.0f,
                RoutingProtocol = "Link State",
                HeartbeatInterval = 0.6f,
                MessageTimeout = 4.0f,
                MaxHops = 8,
                OperationsPerReplica = 6,
                DelayBetweenOperations = 0.5f,
                DelayBetweenSends = 0.25f
            });
            ClearValidation();
        }

        private void ResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            LoadFromConfig(new SimulationConfig());
            ClearValidation();
        }

        #endregion

        private void ShowValidation(string message)
        {
            ValidationMessageTextBlock.Text = "⚠ " + message;
            ValidationMessageTextBlock.Visibility = Visibility.Visible;
        }

        private void ClearValidation()
        {
            ValidationMessageTextBlock.Text = string.Empty;
            ValidationMessageTextBlock.Visibility = Visibility.Collapsed;
        }

        private void StartSimulation_Click(object sender, RoutedEventArgs e)
        {
            ClearValidation();

            // Validate Number of Nodes
            if (!int.TryParse(NumberOfNodesTextBox.Text, out int numberOfNodes) || numberOfNodes < 2 || numberOfNodes > 50)
            {
                ShowValidation("Number of nodes must be an integer between 2 and 50.");
                return;
            }

            // Validate Topology
            string topology = (TopologyComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Random Mesh";

            // Validate Extra Edge Probability
            if (!double.TryParse(ExtraEdgeProbabilityTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double extraEdgeProb) ||
                extraEdgeProb < 0.0 || extraEdgeProb > 1.0)
            {
                ShowValidation("Mesh extra edge probability must be a decimal between 0.0 and 1.0.");
                return;
            }

            // Validate Randomness Seed
            bool useRandomSeed = UseRandomSeedCheckBox.IsChecked == true;
            int seed = 42;
            if (!useRandomSeed)
            {
                if (!int.TryParse(SeedTextBox.Text, out seed) || seed <= 0)
                {
                    ShowValidation("Seed must be a positive integer.");
                    return;
                }
            }

            // Validate Delays
            if (!float.TryParse(MinimumMessageDelayTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float minDelay) || minDelay < 0.01f)
            {
                ShowValidation("Minimum message delay must be at least 0.01 seconds.");
                return;
            }

            if (!float.TryParse(MaximumMessageDelayTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float maxDelay) || maxDelay < minDelay)
            {
                ShowValidation("Maximum message delay must be greater than or equal to minimum message delay.");
                return;
            }

            // Validate Protocol & Timers
            string routingProtocol = (RoutingProtocolComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Flood Routing";

            if (!float.TryParse(HeartbeatIntervalTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float heartbeatInterval) || heartbeatInterval < 0.05f)
            {
                ShowValidation("Heartbeat interval must be at least 0.05 seconds.");
                return;
            }

            if (!float.TryParse(DistanceVectorHeartbeatTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float dvHeartbeatInterval) || dvHeartbeatInterval < 0.05f)
            {
                ShowValidation("Distance vector heartbeat interval must be at least 0.05 seconds.");
                return;
            }

            if (!float.TryParse(MessageTimeoutTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float messageTimeout) || messageTimeout < 0.1f)
            {
                ShowValidation("Message timeout must be at least 0.1 seconds.");
                return;
            }

            if (!float.TryParse(AckTimeoutTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float ackTimeout) || ackTimeout < 0.01f)
            {
                ShowValidation("ACK retry timeout must be at least 0.01 seconds.");
                return;
            }

            if (!int.TryParse(MaxHopsTextBox.Text, out int maxHops) || maxHops < 1)
            {
                ShowValidation("Max hops / TTL must be at least 1.");
                return;
            }

            // Validate Workload
            if (!int.TryParse(OperationsPerReplicaTextBox.Text, out int opsPerReplica) || opsPerReplica < 1 || opsPerReplica > 10000)
            {
                ShowValidation("Operations per replica must be between 1 and 10000.");
                return;
            }

            if (!float.TryParse(DelayBetweenOpsTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float delayBetweenOps) || delayBetweenOps < 0.0f)
            {
                ShowValidation("Delay between operations cannot be negative.");
                return;
            }

            if (!float.TryParse(DelayBetweenSendsTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float delayBetweenSends) || delayBetweenSends < 0.01f)
            {
                ShowValidation("Delay between sends must be at least 0.01 seconds.");
                return;
            }

            // Populate config
            var config = new SimulationConfig
            {
                NumberOfNodes = numberOfNodes,
                Topology = topology,
                ExtraEdgeProbability = extraEdgeProb,
                UseRandomSeed = useRandomSeed,
                Seed = seed,
                MinimumMessageDelay = minDelay,
                MaximumMessageDelay = maxDelay,
                RoutingProtocol = routingProtocol,
                HeartbeatInterval = heartbeatInterval,
                DistanceVectorHeartbeatInterval = dvHeartbeatInterval,
                MessageTimeout = messageTimeout,
                AckTimeout = ackTimeout,
                MaxHops = maxHops,
                OperationsPerReplica = opsPerReplica,
                DelayBetweenOperations = delayBetweenOps,
                DelayBetweenSends = delayBetweenSends
            };

            SimulationConfig.Current = config;

            // Navigate to Simulation Page
            NavigationService.Navigate(new SimulationPage(config));
        }
    }
}