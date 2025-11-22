using OmniMouse.Hooks;
using OmniMouse.MVVM;
using OmniMouse.Network;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Input;
namespace OmniMouse.ViewModel
{
    public class HomePageViewModel : ViewModelBase
    {
        private InputHooks? _hooks;
        private UdpMouseTransmitter? _udp;
        private OmniMouse.Switching.IMultiMachineSwitcher? _switcher;
        private OmniMouse.Switching.NetworkSwitchCoordinator? _switchCoordinator;
        private string _consoleOutput = string.Empty;
        private string _remoteHostIps = string.Empty;  // Comma-separated list of remote hosts
        private bool _isConnected;
        private bool _isMouseSource;  // True if this PC is actively sending input (post-handshake)
        private bool _isPeerConfirmed; // True once handshake/connectivity is confirmed

        private readonly List<string> _consoleLines = new();
        private const int MaxConsoleLines = 20;

        public string ConsoleOutput 
        { 
            get => _consoleOutput;
            set => SetProperty(ref _consoleOutput, value);
        }

        public string RemoteHostIps
        {
            get => _remoteHostIps;
            set => SetProperty(ref _remoteHostIps, value);
        }

        public bool IsMouseSource
        {
            get => _isMouseSource;
            set => SetProperty(ref _isMouseSource, value);
        }

        // Session opened (socket/listening/attempting to connect)
        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value, UpdateButtonStates);
        }

        // Peer confirmed via handshake (ready state)
        public bool IsPeerConfirmed
        {
            get => _isPeerConfirmed;
            set => SetProperty(ref _isPeerConfirmed, value);
        }

        public bool CanConnect => !IsConnected;
        public bool CanDisconnect => IsConnected;

        public ICommand StartAsSourceCommand { get; }
        public ICommand StartAsReceiverCommand { get; }
        public ICommand DisconnectCommand { get; }

        public HomePageViewModel()
        {
            StartAsSourceCommand = new RelayCommand(ExecuteStartAsSource, CanExecuteConnect);
            StartAsReceiverCommand = new RelayCommand(ExecuteStartAsReceiver, CanExecuteConnect);
            DisconnectCommand = new RelayCommand(ExecuteDisconnect, CanExecuteDisconnect);

            App.ConsoleOutputReceived += OnConsoleOutputReceived;
        }

        private void ExecuteStartAsSource(object? parameter)
        {
            _udp = new UdpMouseTransmitter();

            // Wire event BEFORE starting, to avoid missing early role events
            WireRoleChanged(_udp);

            // Session opened; role activation deferred until handshake confirms
            IsConnected = true;
            IsPeerConfirmed = false;
            IsMouseSource = false;
            WriteToConsole("[UI] Host started. Waiting for peer/handshake...");

            _udp.StartHost();
            _udp.LogDiagnostics(); // immediate state dump

            // Hooks will be installed when RoleChanged confirms Sender
        }

        private void ExecuteStartAsReceiver(object? parameter)
        {
            _udp = new UdpMouseTransmitter();

            // Wire event BEFORE starting, to avoid missing early role events
            WireRoleChanged(_udp);

            // Session opened; waiting for handshake confirmation with remote
            IsConnected = true;
            IsPeerConfirmed = false;
            IsMouseSource = false;
            WriteToConsole($"[UI] CoHost started. Attempting handshake to '{RemoteHostIps}'...");

            _udp.StartCoHost(RemoteHostIps);
            _udp.LogDiagnostics(); // immediate state dump
        }

        private void ExecuteDisconnect(object? parameter)
        {
            WriteToConsole("Disconnecting...");
            if (_udp != null)
                _udp.RoleChanged -= OnUdpRoleChanged;

            _hooks?.UninstallHooks();
            _hooks = null;

            _udp?.Disconnect();
            _udp = null;

            IsMouseSource = false;
            IsPeerConfirmed = false;
            IsConnected = false;
        }

        private bool CanExecuteConnect(object? parameter) => !IsConnected;
        private bool CanExecuteDisconnect(object? parameter) => IsConnected;

        private void StartHooks(UdpMouseTransmitter udp)
        {
            // Create and populate the virtual screen map
            var map = new VirtualScreenMap();
            var clientId = Guid.NewGuid().ToString();
            
            // Discover local monitors and add them to the map
            PopulateLocalMonitors(map, clientId);
            
            var inputCoordinator = new InputCoordinator(map, udp, clientId);
            
            // Build simple 1-row machine layout for edge switching: [local, remote]
            try
            {
                var topology = new OmniMouse.Switching.Win32ScreenTopology();
                var mapper = new OmniMouse.Switching.DefaultCoordinateMapper();

                // Use first remote host/IP if comma-separated, else the whole string
                var remoteId = (RemoteHostIps ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                var remote = remoteId.Length > 0 ? remoteId[0].Trim() : string.Empty;

                var machines = string.IsNullOrWhiteSpace(remote)
                    ? new[] { clientId }
                    : new[] { clientId, remote };

                var layout = new OmniMouse.Switching.DefaultMachineLayout(machines, oneRow: true, wrapAround: false)
                {
                    CurrentMachine = clientId
                };

                var policy = new OmniMouse.Switching.DefaultSwitchPolicy(layout, mapper)
                {
                    EdgeThresholdPixels = 2,
                    CooldownMilliseconds = 150,
                    BlockAtCorners = false,
                    UseRelativeMovement = true
                };

                _switcher = new OmniMouse.Switching.MultiMachineSwitcher(topology, layout, policy, mapper);
                _switcher.SetActiveMachine(clientId);
                _switcher.UpdateMatrix(machines, oneRow: true, wrapAround: false);
                _switchCoordinator = new OmniMouse.Switching.NetworkSwitchCoordinator(_switcher, udp, clientId);
                _switcher.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UI] Edge switching init failed: {ex.Message}");
                _switcher = null;
                _switchCoordinator = null;
            }

            _hooks = new InputHooks(udp, inputCoordinator, _switcher);
            _hooks.InstallHooks();
            var thread = new Thread(_hooks.RunMessagePump) { IsBackground = true };
            thread.Start();
        }

        private void PopulateLocalMonitors(VirtualScreenMap map, string clientId)
        {
            // Get local screen information
            var screens = System.Windows.Forms.Screen.AllScreens; // Requires System.Windows.Forms reference
            
            foreach (var screen in screens)
            {
                var monitor = new MonitorInfo
                {
                    OwnerClientId = clientId,
                    FriendlyName = screen.DeviceName,
                    IsPrimary = screen.Primary,
                    LocalBounds = new RectInt(0, 0, screen.Bounds.Width, screen.Bounds.Height),
                    GlobalBounds = new RectInt(
                        screen.Bounds.X, 
                        screen.Bounds.Y, 
                        screen.Bounds.Width, 
                        screen.Bounds.Height
                    )
                };

                // Validate placement (shouldn't fail for local monitors, but good practice)
                if (!map.CanPlaceMonitor(monitor.GlobalBounds))
                {
                    WriteToConsole($"[Warning] Monitor '{monitor.FriendlyName}' overlaps - adjusting layout");
                    // Optionally adjust position or skip
                }
                
                map.AddOrUpdateMonitor(monitor);
                WriteToConsole($"[Layout] Added monitor: {monitor.FriendlyName} at {monitor.GlobalBounds}");
            }
        }

        private void WireRoleChanged(UdpMouseTransmitter udp)
        {
            udp.RoleChanged += OnUdpRoleChanged;
        }

        private void OnUdpRoleChanged(ConnectionRole role)
        {
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                // Handshake/connectivity confirmed at this point
                IsPeerConfirmed = true;

                var becameSource = role == ConnectionRole.Sender;
                IsMouseSource = becameSource;

                WriteToConsole($"[UI][Handshake] Confirmed role: {role}");

                // Align hooks with current role
                if (becameSource)
                {
                    if (_hooks == null && _udp != null)
                    {
                        StartHooks(_udp);
                        WriteToConsole("[UI][RoleConfirm] Sender: hooks installed/active.");
                    }
                }
                else
                {
                    // Keep hooks installed; InputHooks gates sending by role (Receiver ignores send path)
                    // This avoids missing transitions due to uninstall/reinstall churn.
                    WriteToConsole("[UI][RoleConfirm] Receiver: hooks installed; send path gated.");
                }
            }));
        }

        private void WriteToConsole(string message)
        {
            Diagnostics.Logger.Log(message);
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
            if (_udp != null)
                _udp.RoleChanged -= OnUdpRoleChanged;

            _hooks?.UninstallHooks();
            _hooks = null;

            _switcher?.Stop();
            _switcher = null;
            _switchCoordinator = null;

            _udp?.Disconnect();
            _udp = null;

            IsMouseSource = false;
            IsPeerConfirmed = false;
            IsConnected = false;
        }

        public string HostIp
        {
            get => RemoteHostIps;
            set => RemoteHostIps = value;
        }

        // Use receiver mode as the default "Connect" action for UI simplicity
        public ICommand ConnectCommand => StartAsReceiverCommand;
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