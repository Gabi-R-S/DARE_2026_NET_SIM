using System.Windows;

namespace VisualizationTest
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            MainFrame.Navigate(new InputPage());
        }
    }
}
