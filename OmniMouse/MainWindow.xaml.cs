using System.Windows;
using OmniMouse.Hooks;
using OmniMouse.Network;
using OmniMouse.ViewModel;

namespace OmniMouse
{
    public partial class MainWindow : Window
    {
        //private InputHooks? _hooks;
        //private UdpMouseTransmitter? _udp;
        //private bool _isSender = false; // true when this machine is sending input

        public MainWindow()
        {
            InitializeComponent();
            
            // Ensure cleanup happens when window is closed with X button
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Call cleanup on the ViewModel to properly disconnect and notify peer
            if (HomePageControl?.DataContext is HomePageViewModel vm)
            {
                vm.Cleanup();
            }
        }

        private void HomePage_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            // Find the HomePage control and its ViewModel
            if (HomePageControl?.DataContext is HomePageViewModel vm)
            {
                vm.HandleFileDrop(e);
            }
        }

       
    }
}