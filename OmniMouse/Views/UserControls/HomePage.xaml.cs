using OmniMouse.ViewModel;
using System.Windows;
using System.Windows.Controls;

namespace OmniMouse.Views.UserControls
{
    public partial class HomePage : UserControl
    {
        private HomePageViewModel? ViewModel => DataContext as HomePageViewModel;

        public HomePage()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // ViewModel subscribes to console output in its ctor.
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ViewModel?.Cleanup();
        }
    }
}