using System.Windows;
using OmniMouse.Hooks;
using OmniMouse.Network;

namespace OmniMouse
{
    public partial class MainWindow : Window
    {
        private InputHooks? _hooks;
        private UdpMouseTransmitter? _udp;

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
        }

        private void HostButton_Click(object sender, RoutedEventArgs e)
        {
            SetUiEnabled(false);
            Console.WriteLine("Starting in Host mode...");
            _udp = new UdpMouseTransmitter();
            _udp.StartHost();
            StartHooks(_udp);
        }

        private void CohostButton_Click(object sender, RoutedEventArgs e)
        {
            var ip = HostIpBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(ip))
            {
                Console.WriteLine("Please enter a host IP address for cohost mode.");
                return;
            }
            SetUiEnabled(false);
            Console.WriteLine($"Starting in Cohost mode, connecting to {ip}...");
            _udp = new UdpMouseTransmitter();
            _udp.StartCoHost(ip);
            StartHooks(_udp);
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
            _hooks?.UninstallHooks();
            base.OnClosed(e);
        }
    }
}