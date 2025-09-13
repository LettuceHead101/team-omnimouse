using System.Windows;
using OmniMouse.Hooks;
using OmniMouse.Network;

namespace OmniMouse
{
    public partial class MainWindow : Window
    {
        private InputHooks? _hooks;
        private UdpMouseTransmitter? _udp;
        private bool _isSender = false; // true when this machine is sending input

        public MainWindow()
        {
            InitializeComponent();
            App.ConsoleOutputReceived += OnConsoleOutputReceived;
            SetUiEnabled(true);
        }

        private void SetUiEnabled(bool enabled)
        {
            HostButton.IsEnabled = enabled;
            CohostButton.IsEnabled = enabled;
            HostIpBox.IsEnabled = enabled;
            DisconnectButton.IsEnabled = !enabled; // Only enable Disconnect when connected
        }

        // Host = sender (controls the cohost). Requires the cohost's IP in HostIpBox.
        private void HostButton_Click(object sender, RoutedEventArgs e)
        {
            var ip = HostIpBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(ip))
            {
                Console.WriteLine("Please enter the Cohost's IP address before starting Host mode (Host will send input to that IP).");
                return;
            }

            SetUiEnabled(false);
            Console.WriteLine($"Starting in Host mode (sender). Sending input to {ip}...");
            _udp = new UdpMouseTransmitter();
            // StartCoHost configures the transmitter to send to a remote endpoint.
            _udp.StartCoHost(ip);

            // As Host we must capture local input and send it — install hooks.
            StartHooks(_udp);
            _isSender = true;
        }

        // Cohost = receiver (listens for incoming input and injects it). Does not send.
        private void CohostButton_Click(object sender, RoutedEventArgs e)
        {
            SetUiEnabled(false);
            Console.WriteLine("Starting in Cohost mode (receiver). Listening for incoming input...");
            _udp = new UdpMouseTransmitter();
            // StartHost configures the transmitter to listen on the UDP port and inject on receive.
            _udp.StartHost();

            // Do NOT install input hooks here — the Cohost should not send its local input.
            _isSender = false;
        }

        private void StartHooks(UdpMouseTransmitter udp)
        {
            _hooks = new InputHooks(udp);
            _hooks.InstallHooks();

            // Run the message pump on a background thread
            var thread = new System.Threading.Thread(_hooks.RunMessagePump)
            {
                IsBackground = true
            };
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

        protected override void OnClosed(System.EventArgs e)
        {
            App.ConsoleOutputReceived -= OnConsoleOutputReceived;
            if (_isSender)
            {
                // If we installed hooks (because we're a sender/Host), uninstall them.
                _hooks?.UninstallHooks();
            }
            base.OnClosed(e);
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("Disconnecting session...");
            _hooks?.UninstallHooks();
            _udp?.Disconnect();
            _hooks = null;
            _udp = null;
            _isSender = false;
            SetUiEnabled(true);
        }
    }
}