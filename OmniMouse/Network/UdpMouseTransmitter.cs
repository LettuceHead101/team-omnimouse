using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using OmniMouse.Hooks;
using System.Collections.Generic;

namespace OmniMouse.Network
{
    public partial class UdpMouseTransmitter : IUdpMouseTransmitter
    {
        // Fields
        private IUdpClient? _udpClient;
        private IPEndPoint? _remoteEndPoint;
        private const int UdpPort = 5000;
        private readonly Func<int, IUdpClient> _udpClientFactoryWithPort;
        private Thread? _recvThread;
        private volatile bool _running = false;

        // Role management
        private volatile ConnectionRole _currentRole = ConnectionRole.Receiver;
        private volatile bool _handshakeComplete = false;
        private readonly object _roleLock = new object();

        // Receive-loop diagnostics
        private long _recvLoopIterations = 0;
        private DateTime _recvLoopLastAlive = DateTime.MinValue;

        // Protocol/handshake
        private const byte ProtocolVersion = 1;

        // Message type constants
        private const byte MSG_HANDSHAKE_REQUEST = 0x10;
        private const byte MSG_HANDSHAKE_ACCEPT = 0x11;
        private const byte MSG_ROLE_SWITCH_REQUEST = 0x12;
        private const byte MSG_ROLE_SWITCH_ACCEPT = 0x13;
        private const byte MSG_DISCONNECT = 0x14; // Graceful disconnect notification
        private const byte MSG_NORMALIZED_MOUSE = 0x01;
        private const byte MSG_LEGACY_MOUSE = 0x02;
        private const byte MSG_MOUSE = 0x03; // Unified mouse message (absolute or relative with sentinel)
        private const byte MSG_TAKE_CONTROL_AT = 0x05;
        private const byte MSG_TAKE_CONTROL_ACK = 0x06;
        private const byte MSG_MOUSE_BUTTON = 0x07; // Mouse button down/up
        private const byte MSG_MOUSE_WHEEL = 0x08;  // Mouse wheel delta
        private const byte MSG_LAYOUT_ANNOUNCE = 0x40;  // Machine announces position choice
        private const byte MSG_LAYOUT_SYNC = 0x41;      // Full layout broadcast
        private const byte MSG_LAYOUT_UPDATE = 0x42;    // Single machine position update
        private const byte MSG_GRID_LAYOUT_UPDATE = 0x44; // Grid position update (2x2)
        private const byte MSG_MONITOR_INFO = 0x43;     // Monitor information exchange
        private const byte MSG_FILE_OFFER = 0x50;       // File transfer offer notification
        
        // When |X| >= MOVE_MOUSE_RELATIVE && |Y| >= MOVE_MOUSE_RELATIVE, it's a relative delta
        // Actual delta = (value < 0 ? value + MOVE_MOUSE_RELATIVE : value - MOVE_MOUSE_RELATIVE)
        private const int MOVE_MOUSE_RELATIVE = 100000;
        private const int XY_BY_PIXEL = 300000; // Used for switch location markers

        // Handshake state
        private Timer? _hsTimer;
        private int _hsAttempts = 0;
        private long _hsNonceLocal = 0;
        private long _hsNoncePeer = 0;
        private volatile bool _hsInProgress = false;

        // Deterministic tie-breaker identity
        private readonly IPAddress? _localLowestIpV4;

        // Registry to map known client IDs to endpoints
        private readonly Dictionary<string, IPEndPoint> _clientEndpoints = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TcpClient> _tcpConnections = new();
        
        // Store local monitors to send to peer after handshake
        private VirtualScreenMap? _localScreenMap;
        private string? _localClientId;
        private readonly object _tcpLock = new object();
        private const int TcpControlPort = 5001; // Separate port for TCP control messages
        
        // Queue for monitor packets that arrive before screen map is ready
        private readonly Queue<(byte[] data, int offset, IPEndPoint source)> _pendingMonitorPackets = new();
        private readonly object _monitorSyncLock = new object();

        // Events
        public event Action<ConnectionRole>? RoleChanged;
        public event Action<int, int>? TakeControlReceived;
        public event Action<FileShare.FileOfferPacket>? FileOfferReceived;
        public event Action? PeerDisconnected;
        
        /// <summary>
        /// Gets whether the handshake with a peer is complete.
        /// </summary>
        public bool IsHandshakeComplete
        {
            get
            {
                lock (_roleLock) return _handshakeComplete;
            }
        }

        // Seamless switching feature toggle (true = dynamic sender claim via edge detection)
        public bool SeamlessMode { get; set; } = true;

        // Seamless switching feature toggle (true = dynamic sender claim via edge detection)
        public bool SeamlessMode { get; set; } = true;

        // Constructors
        public UdpMouseTransmitter()
            : this(port => new UdpClientAdapter(port)) { }

        public UdpMouseTransmitter(Func<int, IUdpClient> udpClientFactoryWithPort)
        {
            _udpClientFactoryWithPort = udpClientFactoryWithPort ?? throw new ArgumentNullException(nameof(udpClientFactoryWithPort));
            _localLowestIpV4 = GetLowestLocalIPv4();
        }

        // Client registration
        public void RegisterClientEndpoint(string clientId, IPEndPoint endpoint)
        {
            if (string.IsNullOrEmpty(clientId)) throw new ArgumentNullException(nameof(clientId));
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            lock (_clientEndpoints)
            {
                _clientEndpoints[clientId] = endpoint;
            }
            //Console.WriteLine($"[UDP] Registered endpoint for {clientId} -> {endpoint.Address}:{endpoint.Port}");
        }
        
        /// <summary>
        /// Registers the local screen map so monitors can be sent to peer after handshake.
        /// </summary>
        public void RegisterLocalScreenMap(VirtualScreenMap screenMap, string clientId)
        {
            _localScreenMap = screenMap ?? throw new ArgumentNullException(nameof(screenMap));
            _localClientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
            //Console.WriteLine($"[UDP] Registered local screen map with {screenMap.GetMonitorsSnapshot().Count} monitors for clientId={clientId}");

            // Emit consolidated layout summary immediately after local registration
            this.DumpLayoutSummary("[UDP][MonitorSync] Layout after local monitors registered");

            // Process any queued monitor packets that arrived before screen map was ready
            lock (_monitorSyncLock)
            {
                if (_pendingMonitorPackets.Count > 0)
                {
                    //Console.WriteLine($"[UDP][MonitorSync] Processing {_pendingMonitorPackets.Count} queued monitor packet(s)");
                    while (_pendingMonitorPackets.Count > 0)
                    {
                        var (data, offset, source) = _pendingMonitorPackets.Dequeue();
                        HandleMonitorInfo(data, offset);
                    }
                }
            }

            // If handshake already completed, attempt to send monitor info now (previous attempt may have been skipped).
            bool ready;
            lock (_roleLock) ready = _handshakeComplete;
            if (ready)
            {
                Console.WriteLine("[UDP][MonitorSync] Handshake complete; sending monitors after late registration.");
                try { SendMonitorInfo(); } catch (Exception ex) {
                    // Console.WriteLine($"[UDP][MonitorSync] Deferred send failed: {ex.Message}");
                    }

                // Layout after sending local monitors to peer (no change locally, but helpful log)
                this.DumpLayoutSummary("[UDP][MonitorSync] Layout after sending local monitors to peer");
            }
        }
        
        /// <summary>
        /// Registers the local screen map so monitors can be sent to peer after handshake.
        /// </summary>
        public void RegisterLocalScreenMap(VirtualScreenMap screenMap, string clientId)
        {
            _localScreenMap = screenMap ?? throw new ArgumentNullException(nameof(screenMap));
            _localClientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
            Console.WriteLine($"[UDP] Registered local screen map with {screenMap.GetMonitorsSnapshot().Count} monitors for clientId={clientId}");

            // Emit consolidated layout summary immediately after local registration
            this.DumpLayoutSummary("[UDP][MonitorSync] Layout after local monitors registered");

            // Process any queued monitor packets that arrived before screen map was ready
            lock (_monitorSyncLock)
            {
                if (_pendingMonitorPackets.Count > 0)
                {
                    Console.WriteLine($"[UDP][MonitorSync] Processing {_pendingMonitorPackets.Count} queued monitor packet(s)");
                    while (_pendingMonitorPackets.Count > 0)
                    {
                        var (data, offset, source) = _pendingMonitorPackets.Dequeue();
                        HandleMonitorInfo(data, offset);
                    }
                }
            }

            // If handshake already completed, attempt to send monitor info now (previous attempt may have been skipped).
            bool ready;
            lock (_roleLock) ready = _handshakeComplete;
            if (ready)
            {
                Console.WriteLine("[UDP][MonitorSync] Handshake complete; sending monitors after late registration.");
                try { SendMonitorInfo(); } catch (Exception ex) { Console.WriteLine($"[UDP][MonitorSync] Deferred send failed: {ex.Message}"); }
                // Layout after sending local monitors to peer (no change locally, but helpful log)
                this.DumpLayoutSummary("[UDP][MonitorSync] Layout after sending local monitors to peer");
            }
        }

        public void UnregisterClientEndpoint(string clientId)
        {
            if (string.IsNullOrEmpty(clientId)) return;
            lock (_clientEndpoints)
            {
                _clientEndpoints.Remove(clientId);
            }
            //Console.WriteLine($"[UDP] Unregistered endpoint for {clientId}");
        }

        // Connection management
        public void StartHost()
        {
            _udpClient = _udpClientFactoryWithPort(UdpPort);
            //Console.WriteLine($"[UDP] Host listening on {UdpPort}. LocalId={_localLowestIpV4}");

            TryEnsureFirewallRules();
            StartTcpControlListener();

            lock (_roleLock)
            {
                _currentRole = ConnectionRole.Sender;
                _handshakeComplete = false;
            }
            StartReceiveLoop();
        }

        public void StartHost(string peerIp)
        {
            StartHost();
            SetRemotePeer(peerIp);
        }

        public void StartCoHost(string hostIp)
        {
            if (!TryParseIpv4OrResolve(hostIp, out var ip))
                throw new FormatException("Invalid host IP/hostname.");

            _remoteEndPoint = new IPEndPoint(ip.MapToIPv4(), UdpPort);

            _udpClient = _udpClientFactoryWithPort(UdpPort);
            //Console.WriteLine($"[UDP] CoHost using local port {_udpClient.Client.LocalEndPoint} -> {_remoteEndPoint.Address}:{_remoteEndPoint.Port}, LocalId={_localLowestIpV4}");
            lock (_roleLock)
            {
                _currentRole = ConnectionRole.Receiver;
                _handshakeComplete = false;
            }
            StartReceiveLoop();
            TryEnsureFirewallRules();
            StartTcpControlListener();

            BeginHandshake();
        }

        public void StartPeer(string peerIp)
        {
            if (!TryParseIpv4OrResolve(peerIp, out var ip))
                throw new FormatException("Invalid peer IP/hostname.");

            _remoteEndPoint = new IPEndPoint(ip.MapToIPv4(), UdpPort);
            _udpClient = _udpClientFactoryWithPort(UdpPort);
            //Console.WriteLine($"[UDP] Peer mode bound {UdpPort}, remote {_remoteEndPoint.Address}:{_remoteEndPoint.Port}, LocalId={_localLowestIpV4}");
            lock (_roleLock)
            {
                _currentRole = ConnectionRole.Receiver;
                _handshakeComplete = false;
            }
            StartReceiveLoop();
            TryEnsureFirewallRules();
            StartTcpControlListener();

            BeginHandshake();
        }

        public void SetRemotePeer(string hostOrIp)
        {
            if (string.IsNullOrWhiteSpace(hostOrIp))
                throw new ArgumentException("Peer IP/hostname is required.", nameof(hostOrIp));

            if (!TryParseIpv4OrResolve(hostOrIp, out var ip))
                throw new FormatException("Invalid peer IP/hostname.");

            _remoteEndPoint = new IPEndPoint(ip.MapToIPv4(), UdpPort);
            //Console.WriteLine($"[UDP] Remote endpoint set to {_remoteEndPoint.Address}:{_remoteEndPoint.Port}");

            lock (_roleLock)
            {
                _handshakeComplete = false;
            }

            if (_udpClient != null)
            {
                BeginHandshake();
            }
        }

        /// <summary>
        /// Sets the remote peer using a discovered IPEndPoint and initiates handshake.
        /// This is used by mDNS auto-discovery to connect without manual IP entry.
        /// </summary>
        public void SetRemotePeer(IPEndPoint endpoint)
        {
            if (endpoint == null)
                throw new ArgumentNullException(nameof(endpoint));

            _remoteEndPoint = new IPEndPoint(endpoint.Address.MapToIPv4(), endpoint.Port);
            //Console.WriteLine($"[UDP] Remote endpoint set to {_remoteEndPoint.Address}:{_remoteEndPoint.Port} (auto-discovered)");

            lock (_roleLock)
            {
                _handshakeComplete = false;
            }

            if (_udpClient != null)
            {
                BeginHandshake();
            }
        }

        /// <summary>
        /// Starts the UDP client in listening mode without a known peer.
        /// Used with mDNS discovery - peer will be set when discovered.
        /// </summary>
        public void StartListening()
        {
            if (_udpClient != null)
            {
                //Console.WriteLine("[UDP] Already listening");
                return;
            }

            _udpClient = _udpClientFactoryWithPort(UdpPort);
            //Console.WriteLine($"[UDP] Listening on {UdpPort} for auto-discovery. LocalId={_localLowestIpV4}");

            TryEnsureFirewallRules();
            StartTcpControlListener();

            lock (_roleLock)
            {
                _currentRole = ConnectionRole.Receiver;
                _handshakeComplete = false;
            }
            
            StartReceiveLoop();
        }

        public void Disconnect()
        {
            //Console.WriteLine("[UDP] Disconnect requested");
            
            // Send disconnect notification to peer before closing
            try
            {
                if (_udpClient != null && _remoteEndPoint != null && _handshakeComplete)
                {
                    var disconnectMsg = new byte[] { MSG_DISCONNECT };
                    _udpClient.Send(disconnectMsg, disconnectMsg.Length, _remoteEndPoint);
                    //Console.WriteLine($"[UDP] Sent disconnect notification to {_remoteEndPoint}");
                }
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"[UDP] Failed to send disconnect notification: {ex.Message}");
            }

            // Reset all InputHooks state (streaming, edge detection, etc.)
            InputHooks.ResetPeerConnectionState();

            CancelHandshakeTimer();

            _running = false;
            _handshakeComplete = false;
            
            // Reset role to default Receiver state
            lock (_roleLock)
            {
                _currentRole = ConnectionRole.Receiver;
            }
            
            // Clear remote endpoint so we don't try to send more data
            _remoteEndPoint = null;
            
            try { _udpClient?.Close(); } catch { }
            try { _udpClient?.Dispose(); } catch { }
            _udpClient = null;

            // Stop TCP control listener
            StopTcpControlListener();

            // Close any cached outbound TCP connections
            lock (_tcpLock)
            {
                foreach (var kv in _tcpConnections)
                {
                    try { kv.Value.Close(); } catch { }
                }
                _tcpConnections.Clear();
            }

            try
            {
                if (_recvThread != null && _recvThread.IsAlive)
                {
                    if (!_recvThread.Join(500))
                    {
                        // let the background thread exit naturally
                    }
                }
            }
            catch { }
        }

        private void StartReceiveLoop()
        {
            if (_running) return;
            _running = true;
            _recvLoopIterations = 0;
            _recvLoopLastAlive = DateTime.UtcNow;

            _recvThread = new Thread(ReceiveMouseLoopUDP)
            {
                IsBackground = true,
                Name = "UdpMouseTransmitter.ReceiveLoop"
            };

            //Console.WriteLine("[UDP][RecvLoop] Starting thread...");
            _recvThread.Start();
        }

        public void LogDiagnostics()
        {
            var iters = Interlocked.Read(ref _recvLoopIterations);
            //Console.WriteLine($"[UDP][Diag] running={_running}, role={_currentRole}, handshakeComplete={_handshakeComplete}, remoteEP={_remoteEndPoint?.ToString() ?? "<null>"}, attempts={_hsAttempts}, inProgress={_hsInProgress}, iterations={iters}, lastAlive={_recvLoopLastAlive:O}");
        }

        // Add this public property to expose the role (read-only for external consumers)
        public ConnectionRole CurrentRole
        {
            get
            {
                lock (_roleLock)
                {
                    return _currentRole;
                }
            }
        }

        // Add public read-only handshake status
        public bool HandshakeComplete
        {
            get
            {
                lock (_roleLock) return _handshakeComplete;
            }
        }

        // Optional helper: attempt local sender claim (direct role force)
        public bool TryForceSender()
        {
            lock (_roleLock)
            {
                if (!_handshakeComplete) return false;
                if (_currentRole == ConnectionRole.Sender) return true;
                _currentRole = ConnectionRole.Sender;
            }
            //Console.WriteLine("[UDP] Sender role claimed locally.");
            RoleChanged?.Invoke(ConnectionRole.Sender);
            return true;
        }

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int x;
            public int y;
        }

        // Runtime firewall rule provisioning (best-effort; requires elevation)
        private static void TryEnsureFirewallRules()
        {
            try
            {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

                // Use PowerShell New-NetFirewallRule if available; ignore errors (not elevated or rule exists)
                RunPowerShellCommand("if (-not (Get-NetFirewallRule -DisplayName 'OmniMouse UDP 5000' -ErrorAction SilentlyContinue)) { New-NetFirewallRule -DisplayName 'OmniMouse UDP 5000' -Direction Inbound -Protocol UDP -LocalPort 5000 -Action Allow }");
                RunPowerShellCommand("if (-not (Get-NetFirewallRule -DisplayName 'OmniMouse TCP 5001' -ErrorAction SilentlyContinue)) { New-NetFirewallRule -DisplayName 'OmniMouse TCP 5001' -Direction Inbound -Protocol TCP -LocalPort 5001 -Action Allow }");
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"[Firewall] Failed to ensure firewall rules (non-fatal): {ex.Message}");
            }
        }

        private static void RunPowerShellCommand(string psCommand)
        {
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = System.Diagnostics.Process.Start(startInfo);
                if (proc == null) return;
                proc.WaitForExit(5000);
                if (proc.ExitCode != 0)
                {
                    // Likely not elevated or rule already exists; log once for diagnostics.
                    var err = proc.StandardError.ReadToEnd();
                    if (!string.IsNullOrWhiteSpace(err))
                        Console.WriteLine($"[Firewall] PowerShell error (code {proc.ExitCode}): {err.Trim()}");
                }
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"[Firewall] PowerShell invocation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Public helper for direct UDP sends (used by preflight verification).
        /// </summary>
        public void SendDirect(byte[] data, int length)
        {
            if (_udpClient == null || _remoteEndPoint == null)
            {
                throw new InvalidOperationException("UDP client not initialized or remote endpoint unknown");
            }
            _udpClient.Send(data, length, _remoteEndPoint);
        }
    }

    public enum ConnectionRole : byte
    {
        Receiver = 0,
        Sender = 1
    }
}