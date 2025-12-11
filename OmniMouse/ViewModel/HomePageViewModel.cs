using OmniMouse.Hooks;
using OmniMouse.MVVM;
using OmniMouse.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows;
using System.Windows.Input;
namespace OmniMouse.ViewModel
{
    public partial class HomePageViewModel : ViewModelBase
    {
        private InputHooks? _hooks;
        private UdpMouseTransmitter? _udp;
        private MdnsDiscoveryService? _mdns;
        private OmniMouse.Switching.IMultiMachineSwitcher? _switcher;
        private OmniMouse.Switching.NetworkSwitchCoordinator? _switchCoordinator;
        private string _consoleOutput = string.Empty;
        private string _discoveryStatus = "Not connected";
        private bool _isConnected;
        private bool _isMouseSource;  // True if this PC is actively sending input (post-handshake)
        private bool _isPeerConfirmed; // True once handshake/connectivity is confirmed

        private readonly List<string> _consoleLines = new();
        private const int MaxConsoleLines = 50;

        private string? _primaryPeerId;
        private FileShareViewModel? _fileShareViewModel;

        public FileShareViewModel? FileShareViewModel
        {
            get => _fileShareViewModel;
            set => SetProperty(ref _fileShareViewModel, value);
        }

        public string ConsoleOutput 
        { 
            get => _consoleOutput;
            set => SetProperty(ref _consoleOutput, value);
        }

        public string DiscoveryStatus
        {
            get => _discoveryStatus;
            set => SetProperty(ref _discoveryStatus, value);
        }

        private string _manualIpAddress = string.Empty;
        public string ManualIpAddress
        {
            get => _manualIpAddress;
            set => SetProperty(ref _manualIpAddress, value);
        }

        // Backward compatibility for tests
        public string HostIp
        {
            get => _manualIpAddress;
            set => SetProperty(ref _manualIpAddress, value);
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
        public ICommand ShowLayoutCommand { get; }
        public ICommand ConnectToIpCommand { get; }

        private Views.LayoutSelectionView? _layoutWindow; // persistent modeless layout window
        private bool _layoutDialogActive = false;
        private bool _layoutConfigured = false;

        public HomePageViewModel()
        {
            StartAsSourceCommand = new RelayCommand(ExecuteStartAsSource, CanExecuteConnect);
            StartAsReceiverCommand = new RelayCommand(ExecuteStartAsReceiver, CanExecuteConnect);
            DisconnectCommand = new RelayCommand(ExecuteDisconnect, CanExecuteDisconnect);
            ShowLayoutCommand = new RelayCommand(_ => ShowOrCreateLayoutWindow(), _ => _udp?.LayoutCoordinator != null);
            ConnectToIpCommand = new RelayCommand(ExecuteConnectToIp, _ => IsConnected && !string.IsNullOrWhiteSpace(ManualIpAddress));

            // Ensure Upload works even before connection; will log if not connected
            UploadFileCommand ??= new RelayCommand(_ => ExecuteUploadFile());

            App.ConsoleOutputReceived += OnConsoleOutputReceived;
        }

        private void ExecuteStartAsSource(object? parameter)
        {
            _udp = new UdpMouseTransmitter();

            // Wire event BEFORE starting, to avoid missing early role events
            WireRoleChanged(_udp);

            // Start UDP listener (peer will be set when discovered)
            _udp.StartListening();
            _udp.LogDiagnostics();

            // Start mDNS discovery service
            _mdns = new MdnsDiscoveryService();
            _mdns.PeerDiscovered += OnPeerDiscovered;
            _mdns.PeerLost += OnPeerLost;
            _mdns.StatusChanged += OnDiscoveryStatusChanged;
            _mdns.Start();

            // Session opened; role activation deferred until handshake confirms
            IsConnected = true;
            IsPeerConfirmed = false;
            IsMouseSource = false;
            WriteToConsole("[UI] Started. Searching for peers via mDNS...");

            // Initialize file sharing after UDP is started
            InitializeFileSharing();
        }

        private void ExecuteStartAsReceiver(object? parameter)
        {
            _udp = new UdpMouseTransmitter();

            // Wire event BEFORE starting, to avoid missing early role events
            WireRoleChanged(_udp);

            // Start UDP listener (peer will be set when discovered)
            _udp.StartListening();
            _udp.LogDiagnostics();

            // Start mDNS discovery service
            _mdns = new MdnsDiscoveryService();
            _mdns.PeerDiscovered += OnPeerDiscovered;
            _mdns.PeerLost += OnPeerLost;
            _mdns.StatusChanged += OnDiscoveryStatusChanged;
            _mdns.Start();

            // Session opened; waiting for peer discovery and handshake
            IsConnected = true;
            IsPeerConfirmed = false;
            IsMouseSource = false;
            WriteToConsole("[UI] Started. Searching for peers via mDNS...");

            // Initialize file sharing after UDP is started
            InitializeFileSharing();
        }

        private void ExecuteDisconnect(object? parameter)
        {
            WriteToConsole("Disconnecting...");
            
            // Unsubscribe from UDP events
            if (_udp != null)
            {
                _udp.RoleChanged -= OnUdpRoleChanged;
                _udp.PeerDisconnected -= OnPeerDisconnected;
            }

            // Uninstall input hooks
            _hooks?.UninstallHooks();
            _hooks = null;

            // Clean up switching infrastructure
            _switchCoordinator?.Cleanup();
            _switchCoordinator = null;
            
            _switcher?.Stop();
            _switcher = null;

            // Disconnect UDP and reset networking
            _udp?.Disconnect();
            _udp = null;

            // Stop mDNS discovery
            if (_mdns != null)
            {
                _mdns.PeerDiscovered -= OnPeerDiscovered;
                _mdns.PeerLost -= OnPeerLost;
                _mdns.StatusChanged -= OnDiscoveryStatusChanged;
                _mdns.Stop();
                _mdns.Dispose();
                _mdns = null;
            }

            // Close persistent layout window if open
            if (_layoutWindow != null)
            {
                try { _layoutWindow.Close(); } catch { }
                _layoutWindow = null;
            }

            // Reset all state flags
            IsMouseSource = false;
            IsPeerConfirmed = false;
            IsConnected = false;
            _layoutConfigured = false;
            _layoutDialogActive = false;
            _primaryPeerId = null;
            DiscoveryStatus = "Not connected";
            
            WriteToConsole("[UI] Disconnected - all state reset");
        }

        private bool CanExecuteConnect(object? parameter) => !IsConnected;
        private bool CanExecuteDisconnect(object? parameter) => IsConnected;

        private void StartHooks(UdpMouseTransmitter udp)
        {
            // Create and populate the virtual screen map
            var map = new VirtualScreenMap();
            
            // Generate client ID based on actual IP address for consistency
            var clientId = GenerateClientIdFromLocalIp();
            
            // Discover local monitors and add them to the map
            PopulateLocalMonitors(map, clientId);
            
            // Register the screen map with UDP transmitter for monitor synchronization
            udp.RegisterLocalScreenMap(map, clientId);
            
            // Log the client ID being used
            WriteToConsole($"[UI] Client ID set: {clientId}");
            
            var inputCoordinator = new InputCoordinator(map, udp, clientId);
            
            // Build simple 1-row machine layout for edge switching: [local, remote]
            try
            {
                var topology = new OmniMouse.Switching.Win32ScreenTopology();
                var mapper = new OmniMouse.Switching.DefaultCoordinateMapper();

                // Get ordered machine IDs from layout coordinator after user selects positions
                var layoutCoordinator = _udp.GetLayoutCoordinator();
                string[] machines;
                
                if (layoutCoordinator != null && layoutCoordinator.CurrentLayout.Machines.Any(m => m.IsPositioned))
                {
                    // Use positions from layout coordinator (set via UI)
                    machines = layoutCoordinator.CurrentLayout.GetOrderedMachineIds();
                }
                else
                {
                    // Fallback: just use local machine until layout is confirmed
                    machines = new[] { clientId };
                }

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
                
                // UpdateMatrix will be called after layout selection confirms positions
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

        private string GenerateClientIdFromLocalIp()
        {
            try
            {
                // Get lowest local IPv4 address (matching UdpMouseTransmitter._localLowestIpV4 logic)
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                var localIp = host.AddressList
                    .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork 
                              && !IPAddress.IsLoopback(ip) 
                              && !ip.ToString().StartsWith("169.254.")) // Exclude link-local
                    .OrderBy(ip => ip.GetAddressBytes()[0])
                    .ThenBy(ip => ip.GetAddressBytes()[1])
                    .ThenBy(ip => ip.GetAddressBytes()[2])
                    .ThenBy(ip => ip.GetAddressBytes()[3])
                    .FirstOrDefault();
                
                if (localIp != null)
                {
                    return $"{localIp}:5000";
                }
            }
            catch (Exception ex)
            {
                WriteToConsole($"[UI] Warning: Could not determine local IP: {ex.Message}");
            }
            
            // Fallback to GUID if IP detection fails
            return Guid.NewGuid().ToString();
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
            udp.PeerDisconnected += OnPeerDisconnected;
        }

        private void OnPeerDiscovered(IPEndPoint endpoint, string peerId)
        {
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                WriteToConsole($"[UI][mDNS] Peer discovered: {endpoint.Address} (ID: {peerId})");
                
                if (_udp != null)
                {
                    // Ignore peer discoveries if already connected (prevent reconnection loops)
                    if (_udp.IsHandshakeComplete)
                    {
                        WriteToConsole($"[UI][mDNS] Already connected - ignoring peer discovery");
                        return;
                    }
                    
                    // Set the remote peer and initiate handshake
                    _udp.SetRemotePeer(endpoint);
                    WriteToConsole($"[UI][mDNS] Initiating handshake with {endpoint.Address}...");
                }
            }));
        }

        private void OnPeerLost(string peerId)
        {
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                WriteToConsole($"[UI][mDNS] Peer lost: {peerId}");
            }));
        }

        private void OnDiscoveryStatusChanged(string status)
        {
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                DiscoveryStatus = status;
            }));
        }

        private void ExecuteConnectToIp(object? parameter)
        {
            if (_udp == null || string.IsNullOrWhiteSpace(ManualIpAddress))
                return;

            try
            {
                WriteToConsole($"[UI] Manual connection to {ManualIpAddress}...");
                
                // Parse IP and create endpoint
                if (!IPAddress.TryParse(ManualIpAddress.Trim(), out var ip))
                {
                    WriteToConsole($"[UI][ERROR] Invalid IP address: {ManualIpAddress}");
                    DiscoveryStatus = $"Invalid IP: {ManualIpAddress}";
                    return;
                }

                var endpoint = new IPEndPoint(ip, 5000);
                
                // Use the same method that mDNS discovery uses
                _udp.SetRemotePeer(endpoint);
                
                DiscoveryStatus = $"Connecting to {ManualIpAddress}...";
                WriteToConsole($"[UI] Handshake initiated with {ManualIpAddress}");
            }
            catch (Exception ex)
            {
                WriteToConsole($"[UI][ERROR] Failed to connect: {ex.Message}");
                DiscoveryStatus = $"Connection failed: {ex.Message}";
            }
        }

        private void OnPeerDisconnected()
        {
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                WriteToConsole("[UI][Disconnect] Peer disconnected gracefully");
                
                // Reset connection state but keep IsConnected true so we can reconnect
                IsPeerConfirmed = false;
                IsMouseSource = false;
                _layoutConfigured = false;
                _primaryPeerId = null;
                
                WriteToConsole("[UI][Disconnect] State reset - waiting for peer to reconnect...");
            }));
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

                // Install hooks for BOTH roles (edge detection needs to work for both Sender and Receiver)
                if (_hooks == null && _udp != null)
                {
                    StartHooks(_udp);
                    WriteToConsole($"[UI][RoleConfirm] {role}: hooks installed for edge detection.");
                    
                    // Only show layout dialog when hooks are first installed
                    if (!_layoutConfigured && !_layoutDialogActive)
                    {
                        ShowLayoutSelectionDialog();
                    }
                }
                else
                {
                    WriteToConsole($"[UI][RoleConfirm] {role}: hooks already installed.");
                }
            }));
        }

        private void ShowLayoutSelectionDialog()
        {
            if (_layoutDialogActive)
            {
                WriteToConsole("[UI][Layout] Dialog already active; suppressing duplicate.");
                return;
            }

            // Suppress dialog if persistent window already open
            if (_layoutWindow != null && _layoutWindow.IsVisible)
            {
                WriteToConsole("[UI][Layout] Persistent layout window open; using it instead of dialog.");
                return;
            }

            if (_udp?.LayoutCoordinator == null)
            {
                WriteToConsole("[UI][Layout] Layout coordinator not initialized yet.");
                return;
            }

            // Get local machine ID from the UDP transmitter
            string localMachineId = _udp.GetLocalMachineId();
            var viewModel = new LayoutSelectionViewModel(_udp.LayoutCoordinator, localMachineId);
            var dialog = new Views.LayoutSelectionView(viewModel)
            {
                Owner = Application.Current.MainWindow
            };
            dialog.IsDialogInstance = true; // mark as modal instance

            _layoutDialogActive = true;
            bool? result = null;
            try
            {
                result = dialog.ShowDialog();
            }
            finally
            {
                _layoutDialogActive = false;
            }

            if (result == true)
            {
                _layoutConfigured = true; // setting the flag to true here so it doesn't pop up again on repeated calls

                WriteToConsole("[UI][Layout] Layout confirmed. Edge detection active.");
                if (_switcher != null && _udp?.GetLayoutCoordinator() != null)
                {
                    var orderedMachineIds = _udp.GetLayoutCoordinator()!.CurrentLayout.GetOrderedMachineIds();
                    _switcher.UpdateMatrix(orderedMachineIds, oneRow: true, wrapAround: false);
                    WriteToConsole($"[UI][Layout] Updated switcher with {orderedMachineIds.Length} machines: {string.Join(", ", orderedMachineIds)}");

                    // Determine remote peer (first non-local id)
                    var localId = _udp.GetLocalMachineId();
                    _primaryPeerId = orderedMachineIds.FirstOrDefault(id => id != localId);
                    if (!string.IsNullOrEmpty(_primaryPeerId))
                    {
                        InputHooks.SetRemotePeer(_primaryPeerId);
                        WriteToConsole($"[UI][Layout] Remote peer set for edge claim: {_primaryPeerId}");
                    }
                    else
                    {
                        WriteToConsole("[UI][Layout] Warning: no remote peer found for edge claim.");
                    }
                }
            }
            else
            {
                WriteToConsole("[UI][Layout] Layout selection cancelled.");
            }
        }

        private void ShowOrCreateLayoutWindow()
        {
            if (_udp?.LayoutCoordinator == null)
            {
                WriteToConsole("[UI][Layout] Cannot show layout window - coordinator not ready.");
                return;
            }

            if (_layoutDialogActive)
            {
                WriteToConsole("[UI][Layout] Modal dialog active; not opening persistent window.");
                return;
            }

            // Reuse existing window if still open
            if (_layoutWindow != null && _layoutWindow.IsVisible)
            {
                _layoutWindow.Activate();
                return;
            }

            var localId = _udp.GetLocalMachineId();
            var vm = new LayoutSelectionViewModel(_udp.LayoutCoordinator, localId);
            _layoutWindow = new Views.LayoutSelectionView(vm)
            {
                Owner = Application.Current.MainWindow,
                ShowInTaskbar = true,
                Topmost = false
            };
            // modeless: do NOT set IsDialogInstance
            _layoutWindow.Closed += (s, e) => _layoutWindow = null;
            _layoutWindow.Show();
            WriteToConsole("[UI][Layout] Persistent layout window opened.");
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
            {
                _udp.RoleChanged -= OnUdpRoleChanged;
                _udp.PeerDisconnected -= OnPeerDisconnected;
            }

            _hooks?.UninstallHooks();
            _hooks = null;

            _switchCoordinator?.Cleanup();
            _switchCoordinator = null;
            
            _switcher?.Stop();
            _switcher = null;

            _udp?.Disconnect();
            _udp = null;

            // Cleanup mDNS service
            if (_mdns != null)
            {
                _mdns.PeerDiscovered -= OnPeerDiscovered;
                _mdns.PeerLost -= OnPeerLost;
                _mdns.StatusChanged -= OnDiscoveryStatusChanged;
                _mdns.Stop();
                _mdns.Dispose();
                _mdns = null;
            }

            // Close persistent layout window
            if (_layoutWindow != null)
            {
                try { _layoutWindow.Close(); } catch { }
                _layoutWindow = null;
            }

            IsMouseSource = false;
            IsPeerConfirmed = false;
            IsConnected = false;
            _layoutConfigured = false;
            _layoutDialogActive = false;
            _primaryPeerId = null;
            DiscoveryStatus = "Not connected";

            // Cleanup file sharing resources
            CleanupFileSharing();
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