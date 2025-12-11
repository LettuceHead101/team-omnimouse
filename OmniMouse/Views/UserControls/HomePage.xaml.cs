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

        private void HomePage_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DragOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                e.Effects = DragDropEffects.None;
                DragOverlay.Visibility = Visibility.Collapsed;
            }
            e.Handled = true;
        }

        private void HomePage_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DragOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                e.Effects = DragDropEffects.None;
                DragOverlay.Visibility = Visibility.Collapsed;
            }
            e.Handled = true;
        }

        private void HomePage_DragLeave(object sender, DragEventArgs e)
        {
            DragOverlay.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }

        private void HomePage_Drop(object sender, DragEventArgs e)
        {
            DragOverlay.Visibility = Visibility.Collapsed;
            ViewModel?.HandleFileDrop(e);
            e.Handled = true;
        }
    }
}