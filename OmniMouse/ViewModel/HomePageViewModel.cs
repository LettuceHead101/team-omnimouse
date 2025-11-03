using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Threading;
using System.Threading.Tasks;
using OmniMouse.Hooks;
using OmniMouse.MVVM;
using OmniMouse.Network;
using OmniMouse.Model;
using OmniMouse.Storage;

namespace OmniMouse.ViewModel
{
    public class HomePageViewModel : ViewModelBase
    {
        private InputHooks? _hooks;
        private UdpMouseTransmitter? _udp;
        private PeerDiscovery? _discovery;
        private string _consoleOutput = string.Empty;
        private string _hostIp = string.Empty;
        private bool _isConnected;

        private readonly List<string> _consoleLines = new();
        private const int MaxConsoleLines = 20;

        public string ConsoleOutput
        {
            get => _consoleOutput;
            set => SetProperty(ref _consoleOutput, value);
        }

        public string HostIp
        {
            get => _hostIp;
            set => SetProperty(ref _hostIp, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value, UpdateButtonStates);
        }

        public bool CanConnect => !IsConnected;
        public bool CanDisconnect => IsConnected;

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }

        public HomePageViewModel()
        {
            // Use async connect flow (no-IP required)
            ConnectCommand = new RelayCommand(async _ => await ExecuteConnectAsync(), CanExecuteConnect);
            DisconnectCommand = new RelayCommand(ExecuteDisconnect, CanExecuteDisconnect);

            App.ConsoleOutputReceived += OnConsoleOutputReceived;
        }

        // New: zero-friction connect
        private async Task ExecuteConnectAsync()
        {
            // Manual override still supported; also persist it for next launch
            if (!string.IsNullOrWhiteSpace(HostIp))
            {
                WriteToConsole($"Connecting to peer at {HostIp} (manual)...");
                BeginUdpAndHooks(HostIp);

                // Save manual peer so future launches can auto-connect without typing
                KnownPeersStore.Upsert(new PeerInfo
                {
                    Id = Guid.Empty,           // unknown without discovery; Upsert matches by IP as well
                    Name = HostIp,
                    Ip = HostIp,
                    Port = 5000,
                    LastSeenUtc = DateTime.UtcNow
                });
                return;
            }

            // 1) Try known peers first
            var known = KnownPeersStore.Load();
            foreach (var kp in known)
            {
                if (TryBeginUdp(kp.Ip))
                {
                    WriteToConsole($"Connecting to known peer {kp.Name} at {kp.Ip}...");
                    StartHooks(_udp!);
                    IsConnected = true;
                    return;
                }
            }

            // 2) Auto-discover on LAN
            WriteToConsole("Discovering peers on LAN...");
            _discovery = new PeerDiscovery();
            _discovery.Start();

            var peer = await _discovery.WaitForFirstPeerAsync(TimeSpan.FromSeconds(3));
            if (peer == null)
            {
                WriteToConsole("No peers discovered. You can still enter an IP manually.");
                _discovery.Dispose(); _discovery = null;
                return;
            }

            WriteToConsole($"Discovered peer {peer.Name} at {peer.Ip}. Connecting...");
            KnownPeersStore.Upsert(peer); // persist for next time
            BeginUdpAndHooks(peer.Ip);
        }

        private void BeginUdpAndHooks(string ip)
        {
            _udp = new UdpMouseTransmitter();
            _udp.StartPeer(ip);
            StartHooks(_udp);
            IsConnected = true;
        }

        private bool TryBeginUdp(string ip)
        {
            try
            {
                _udp = new UdpMouseTransmitter();
                _udp.StartPeer(ip);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AutoConnect] Failed to start peer to {ip}: {ex.Message}");
                try { _udp?.Disconnect(); } catch { }
                _udp = null;
                return false;
            }
        }

        private void ExecuteDisconnect(object? parameter)
        {
            WriteToConsole("Disconnecting session...");
            _hooks?.UninstallHooks();
            _udp?.Disconnect();
            _discovery?.Dispose();
            _hooks = null;
            _udp = null;
            _discovery = null;
            IsConnected = false;
        }

        private bool CanExecuteConnect(object? parameter) => !IsConnected;
        private bool CanExecuteDisconnect(object? parameter) => IsConnected;

        private void StartHooks(UdpMouseTransmitter udp)
        {
            _hooks = new InputHooks(udp);
            _hooks.InstallHooks();
            var thread = new Thread(_hooks.RunMessagePump) { IsBackground = true };
            thread.Start();
        }

        private void WriteToConsole(string message)
        {
            Console.WriteLine(message);
        }

        private void OnConsoleOutputReceived(string text)
        {
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                if (string.IsNullOrEmpty(text)) return;

                var normalized = text.Replace("\r", "");
                var parts = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in parts)
                {
                    _consoleLines.Add(line);
                    while (_consoleLines.Count > MaxConsoleLines)
                        _consoleLines.RemoveAt(0);
                }

                ConsoleOutput = string.Join(Environment.NewLine, _consoleLines);
            }));
        }

        private void UpdateButtonStates()
        {
            CommandManager.InvalidateRequerySuggested();
            OnPropertyChanged(nameof(CanConnect));
            OnPropertyChanged(nameof(CanDisconnect));
        }

        public void Cleanup()
        {
            App.ConsoleOutputReceived -= OnConsoleOutputReceived;
            _hooks?.UninstallHooks();
            _udp?.Disconnect();
            _discovery?.Dispose();
            _hooks = null;
            _udp = null;
            _discovery = null;
        }
    }

    public sealed class RelayCommand : ICommand
    {
        private static readonly Predicate<object?> s_true = _ => true;
        private readonly Action<object?> _execute;
        private readonly Predicate<object?> _canExecute;

        public RelayCommand(Action<object?> execute)
            : this(execute, s_true) { }

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute ?? s_true;
        }

        public bool CanExecute(object? parameter) => _canExecute(parameter);
        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}