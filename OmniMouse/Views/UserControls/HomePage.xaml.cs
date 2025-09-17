using OmniMouse.Hooks;
using OmniMouse.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace OmniMouse.Views.UserControls
{
    public partial class HomePage : UserControl
    {
        private InputHooks? _hooks;
        private UdpMouseTransmitter? _udp;
        private bool _isSender = false;

        public HomePage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object? s, RoutedEventArgs e)
        {
            App.ConsoleOutputReceived += OnConsoleOutputReceived;
            SetUiEnabled(true);
        }

        private void OnUnloaded(object? s, RoutedEventArgs e)
        {
            App.ConsoleOutputReceived -= OnConsoleOutputReceived;
            if (_isSender) _hooks?.UninstallHooks();
            _udp?.Disconnect();
            _hooks = null; _udp = null; _isSender = false;
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
            _isSender = true;
        }

        private void CohostButton_Click(object sender, RoutedEventArgs e)
        {
            SetUiEnabled(false);
            Console.WriteLine("Starting Cohost (receiver). Listening...");
            _udp = new UdpMouseTransmitter();
            _udp.StartHost();
            _isSender = false;
        }

        private void StartHooks(UdpMouseTransmitter udp)
        {
            _hooks = new InputHooks(udp);
            _hooks.InstallHooks();
            var thread = new System.Threading.Thread(_hooks.RunMessagePump) { IsBackground = true };
            thread.Start();
        }

        private void OnConsoleOutputReceived(string text)
        {
            Dispatcher.Invoke(() =>
            {
                ConsoleOutputBox.AppendText(text);
                ConsoleOutputBox.ScrollToEnd();
            });
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("Disconnecting session...");
            _hooks?.UninstallHooks();
            _udp?.Disconnect();
            _hooks = null; _udp = null; _isSender = false;
            SetUiEnabled(true);
        }
    }
}