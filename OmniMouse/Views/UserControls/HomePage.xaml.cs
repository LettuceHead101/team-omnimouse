using OmniMouse.Hooks;
using OmniMouse.Network;
using OmniMouse.ViewModel;
using System;
using System.Windows;
using System.Windows.Controls;

namespace OmniMouse.Views.UserControls
{
    public partial class HomePage : UserControl
    {
        private InputHooks? _hooks;
        private UdpMouseTransmitter? _udp;

        private HomePageViewModel? ViewModel => DataContext as HomePageViewModel;
        public HomePage()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // No need to add event handlers as they're now in the ViewModel
            // The ViewModel constructor already subscribes to console output
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // Clean up resources in the ViewModel
            ViewModel?.Cleanup();
        }

        private void SetUiEnabled(bool enabled)
        {
            HostButton.IsEnabled = enabled;
            CohostButton.IsEnabled = enabled;
            HostIpBox.IsEnabled = enabled;
            DisconnectButton.IsEnabled = !enabled;
        }

        private void HostButton_Click(object sender, RoutedEventArgs e)
        {
            var ip = HostIpBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(ip))
            {
                Console.WriteLine("Enter the Cohost's IP before starting Host mode.");
                return;
            }

            SetUiEnabled(false);
            Console.WriteLine($"Starting Host (sender). Sending to {ip}...");
            _udp = new UdpMouseTransmitter();
            _udp.StartCoHost(ip);
            StartHooks(_udp);
        }

        private void CohostButton_Click(object sender, RoutedEventArgs e)
        {
            SetUiEnabled(false);
            Console.WriteLine("Starting Cohost (receiver). Listening...");
            _udp = new UdpMouseTransmitter();
            _udp.StartHost();
        }

        private void StartHooks(UdpMouseTransmitter udp)
        {
            _hooks = new InputHooks(udp);
            _hooks.InstallHooks();
            var thread = new System.Threading.Thread(_hooks.RunMessagePump) { IsBackground = true };
            thread.Start();
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("Disconnecting session...");
            _hooks?.UninstallHooks();
            _udp?.Disconnect();
            _hooks = null; _udp = null;
            SetUiEnabled(true);
        }
    }
}