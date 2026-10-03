using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace VisualizationTest
{
    public partial class InputPage : Page
    {
        public InputPage()
        {
            InitializeComponent();
        }

        private void StartSimulation_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(NumberOfNodesTextBox.Text, out int numberOfNodes))
            {
                MessageBox.Show("Number of nodes must be a valid integer.");
                return;
            }

            if (!float.TryParse(MinimumMessageDelayTextBox.Text, out float minimumMessageDelay))
            {
                MessageBox.Show("Minimum message delay must be a valid integer.");
                return;
            }

            if (!float.TryParse(MaximumMessageDelayTextBox.Text, out float maximumMessageDelay))
            {
                MessageBox.Show("Maximum message delay must be a valid integer.");
                return;
            }

            // Optional additional validation
            if (numberOfNodes <= 0)
            {
                MessageBox.Show("Number of nodes must be greater than 0.");
                return;
            }

            if (minimumMessageDelay < 0 || maximumMessageDelay < 0)
            {
                MessageBox.Show("Message delays cannot be negative.");
                return;
            }

            if (minimumMessageDelay > maximumMessageDelay)
            {
                MessageBox.Show("Minimum delay cannot be greater than maximum delay.");
                return;
            }

            // Navigate to the simulation page
            NavigationService.Navigate(
                new SimulationPage(
                    numberOfNodes,
                    minimumMessageDelay,
                    maximumMessageDelay
                )
            );
        }
    }
}