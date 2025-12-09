using System;
using System.Net;
using System.Net.Sockets; // for  AddressFamily
using System.Threading;
using System.Runtime.InteropServices;
using OmniMouse.Hooks; // for feedback-loop suppression

namespace OmniMouse.Network
{
    public class UdpMouseTransmitter : IUdpMouseTransmitter
    {
        private IUdpClient? _udpClient;
        private IPEndPoint? _remoteEndPoint;
        private string _hostIp = "";
        private const int UdpPort = 5000;
        private readonly Func<int, IUdpClient> _udpClientFactoryWithPort;
        private readonly Action<int, int> _setCursorPos;
        private Thread? _recvThread;
        private volatile bool _running = false;

        // Role management for handshake and dynamic switching
        private volatile ConnectionRole _currentRole = ConnectionRole.Receiver;
        private volatile bool _handshakeComplete = false;
        private readonly object _roleLock = new object();

        // Receive-loop diagnostics
        private long _recvLoopIterations = 0;
        private DateTime _recvLoopLastAlive = DateTime.MinValue;

        // Message type constants
        private const byte MSG_HANDSHAKE_REQUEST = 0x10;  // Request to establish roles
        private const byte MSG_HANDSHAKE_ACCEPT = 0x11;   // Accept proposed roles
        private const byte MSG_ROLE_SWITCH_REQUEST = 0x12; // Request role swap (at edge)
        private const byte MSG_ROLE_SWITCH_ACCEPT = 0x13; // Confirm role swap
        private const byte MSG_NORMALIZED_MOUSE = 0x01;   // Normalized mouse data
        private const byte MSG_LEGACY_MOUSE = 0x02;       // Legacy raw pixel data

        public event Action<ConnectionRole>? RoleChanged;

        public UdpMouseTransmitter()
            : this(port => new UdpClientAdapter(port), NativeSetCursorPos) { }

        public UdpMouseTransmitter(Func<int, IUdpClient> udpClientFactoryWithPort)
            : this(udpClientFactoryWithPort, NativeSetCursorPos) { }

        public UdpMouseTransmitter(Func<int, IUdpClient> udpClientFactoryWithPort,
                                   Action<int, int> setCursorPos)
        {
            _udpClientFactoryWithPort = udpClientFactoryWithPort ?? throw new ArgumentNullException(nameof(udpClientFactoryWithPort));
            _setCursorPos = setCursorPos ?? NativeSetCursorPos;
        }

                private void ProcessMessage(byte msgType, byte[] data, IPEndPoint remoteEP)
        {
            switch (msgType)
            {
                case 0x21: // PRE-FLIGHT ACK - NEW
                    OmniMouse.Hooks.InputHooks.OnPreFlightAckReceived();
                    Console.WriteLine("[UDP][PreFlight] ACK received from peer");
                    break;
                // ...existing cases...
            }
        }

        // Public helper for direct sends (used by preflight)
        public void SendDirect(byte[] data, int length)
        {
            if (_udpClient == null || _remoteEndPoint == null)
            {
                throw new InvalidOperationException("UDP client not initialized or remote endpoint unknown");
            }
            _udpClient.Send(data, length, _remoteEndPoint);
        }

        public void StartHost()
        {
            _udpClient = _udpClientFactoryWithPort(UdpPort);
            Console.WriteLine($"[UDP] Host listening on {UdpPort}.");
            lock (_roleLock)
            {
                _currentRole = ConnectionRole.Sender; // Host defaults to sender
                _handshakeComplete = false;
            }
            StartReceiveLoop();
        }

        public void StartCoHost(string hostIp)
        {
            _hostIp = hostIp;

            if (!IPAddress.TryParse(hostIp, out var ip))
                ip = Dns.GetHostAddresses(hostIp).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                     ?? throw new FormatException("Invalid host IP/hostname.");
            _remoteEndPoint = new IPEndPoint(ip, UdpPort);

            _udpClient = _udpClientFactoryWithPort(UdpPort);
            Console.WriteLine($"[UDP] CoHost sender using local port {_udpClient.Client.LocalEndPoint} -> sending to {_remoteEndPoint.Address}:{_remoteEndPoint.Port}");
            lock (_roleLock)
            {
                _currentRole = ConnectionRole.Receiver; // CoHost defaults to receiver
                _handshakeComplete = false;
            }
            StartReceiveLoop();

            // Initiate handshake after brief delay
            Thread.Sleep(500);
            SendHandshakeRequest();
        }

        // Symmetric mode: bind to UdpPort and send to peerIp:UdpPort
        public void StartPeer(string peerIp)
        {
            _hostIp = peerIp;
            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(peerIp), UdpPort);

            _udpClient = _udpClientFactoryWithPort(UdpPort); // bind to well-known port for receiving
            Console.WriteLine($"[UDP] Peer mode bound to {UdpPort}, remote {_remoteEndPoint.Address}:{_remoteEndPoint.Port}");
            lock (_roleLock)
            {
                _currentRole = ConnectionRole.Receiver; // Peer defaults to receiver
                _handshakeComplete = false;
            }
            StartReceiveLoop();

            // Initiate handshake after brief delay
            Thread.Sleep(500);
            SendHandshakeRequest();
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

            Console.WriteLine("[UDP][RecvLoop] Starting thread...");
            _recvThread.Start();
        }

        // Send handshake request to establish roles
        private void SendHandshakeRequest()
        {
            if (_udpClient == null || _remoteEndPoint == null) return;

            var buf = new byte[2];
            buf[0] = MSG_HANDSHAKE_REQUEST;
            buf[1] = (byte)_currentRole; // Propose our current role

            try
            {
                _udpClient.Send(buf, buf.Length, _remoteEndPoint);
                Console.WriteLine($"[UDP][Handshake] Sent request as {_currentRole}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UDP][Handshake] Failed to send request: {ex.Message}");
            }
        }

        // Request role switch when at screen edge
        public void RequestRoleSwitch()
        {
            if (_udpClient == null || _remoteEndPoint == null || !_handshakeComplete) return;

            var buf = new byte[1];
            buf[0] = MSG_ROLE_SWITCH_REQUEST;

            try
            {
                _udpClient.Send(buf, buf.Length, _remoteEndPoint);
                Console.WriteLine($"[UDP][RoleSwitch] Requested role switch from {_currentRole}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UDP][RoleSwitch] Failed to send request: {ex.Message}");
            }
        }

        // Handle incoming handshake request
        private void HandleHandshakeRequest(byte[] data)
        {
            if (data.Length < 2) return;

            var proposedRemoteRole = (ConnectionRole)data[1];
            var acceptedLocalRole = proposedRemoteRole == ConnectionRole.Sender
                ? ConnectionRole.Receiver
                : ConnectionRole.Sender;

            Console.WriteLine($"[UDP][Handshake] Request received. Remote proposed: {proposedRemoteRole}. Assigning local: {acceptedLocalRole}");

            lock (_roleLock)
            {
                _currentRole = acceptedLocalRole;
                _handshakeComplete = true;
            }

#if DEBUG
            if (proposedRemoteRole == ConnectionRole.Receiver)
                System.Diagnostics.Debug.Assert(_currentRole == ConnectionRole.Sender, "[Handshake] Host should resolve to Sender.");
#endif

            // Send acceptance
            var buf = new byte[2];
            buf[0] = MSG_HANDSHAKE_ACCEPT;
            buf[1] = (byte)_currentRole;

            try
            {
                if (_remoteEndPoint != null)
                {
                    _udpClient?.Send(buf, buf.Length, _remoteEndPoint);
                    Console.WriteLine($"[UDP][Handshake] Accepted. Role set to {_currentRole}");
                    RoleChanged?.Invoke(_currentRole);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UDP][Handshake] Failed to send acceptance: {ex.Message}");
            }
        }

        // Handle handshake acceptance
        private void HandleHandshakeAccept(byte[] data)
        {
            if (data.Length < 2) return;

            var confirmedRemoteRole = (ConnectionRole)data[1];
            var confirmedLocalRole = confirmedRemoteRole == ConnectionRole.Sender
                ? ConnectionRole.Receiver
                : ConnectionRole.Sender;

            Console.WriteLine($"[UDP][Handshake] Acceptance received. Remote confirmed: {confirmedRemoteRole}. Setting local: {confirmedLocalRole}");

            lock (_roleLock)
            {
                _currentRole = confirmedLocalRole;
                _handshakeComplete = true;
            }

#if DEBUG
            if (confirmedRemoteRole == ConnectionRole.Sender)
                System.Diagnostics.Debug.Assert(_currentRole == ConnectionRole.Receiver, "[Handshake] CoHost should resolve to Receiver.");
#endif

            Console.WriteLine($"[UDP][Handshake] Complete. Role confirmed as {_currentRole}");
            RoleChanged?.Invoke(_currentRole);
        }

        // Handle role switch request
        private void HandleRoleSwitchRequest()
        {
            lock (_roleLock)
            {
                // Switch roles
                _currentRole = _currentRole == ConnectionRole.Sender
                    ? ConnectionRole.Receiver
                    : ConnectionRole.Sender;

                Console.WriteLine($"[UDP][RoleSwitch] Role switched to {_currentRole}");
                RoleChanged?.Invoke(_currentRole);
            }

            // Send acceptance
            var buf = new byte[1];
            buf[0] = MSG_ROLE_SWITCH_ACCEPT;

            try
            {
                if (_remoteEndPoint != null)
                {
                    _udpClient?.Send(buf, buf.Length, _remoteEndPoint);
                    Console.WriteLine($"[UDP][RoleSwitch] Accepted and confirmed");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UDP][RoleSwitch] Failed to send acceptance: {ex.Message}");
            }
        }

        // Handle role switch acceptance
        private void HandleRoleSwitchAccept()
        {
            lock (_roleLock)
            {
                // Complete the switch
                _currentRole = _currentRole == ConnectionRole.Sender
                    ? ConnectionRole.Receiver
                    : ConnectionRole.Sender;

                Console.WriteLine($"[UDP][RoleSwitch] Confirmed. Now {_currentRole}");
                RoleChanged?.Invoke(_currentRole);
            }
        }

        // Send raw pixel ints (legacy)
        public void SendMousePosition(int x, int y)
        {
            // Only send if we are the sender
            lock (_roleLock)
            {
                if (!_handshakeComplete || _currentRole != ConnectionRole.Sender)
                    return;
            }

            if (_udpClient == null) return;
            if (_remoteEndPoint != null)
            {
                // simple legacy format: prefix 0x02, then two 4-byte ints
                var buf = new byte[1 + 8];
                buf[0] = MSG_LEGACY_MOUSE;
                Array.Copy(BitConverter.GetBytes(x), 0, buf, 1, 4);
                Array.Copy(BitConverter.GetBytes(y), 0, buf, 1 + 4, 4);
                _udpClient.Send(buf, buf.Length, _remoteEndPoint);
            }
        }

        // NEW: send two normalized floats (0..1). Message type 0x01 then 8 bytes of floats.
        public void SendNormalizedMousePosition(float normalizedX, float normalizedY)
        {
            // Only send if we are the sender
            lock (_roleLock)
            {
                if (!_handshakeComplete || _currentRole != ConnectionRole.Sender)
                {
                    Console.WriteLine($"[UDP][SendNormalized][SKIP] handshakeComplete={_handshakeComplete}, role={_currentRole}");
                    return;
                }
            }

            if (_udpClient == null || _remoteEndPoint == null) return;

            // Log what we're about to send so you can verify sender values
            Console.WriteLine($"[UDP][SendNormalized] -> nx={normalizedX:F6}, ny={normalizedY:F6} to {_remoteEndPoint.Address}:{_remoteEndPoint.Port}");

            var buf = new byte[1 + 4 + 4];
            buf[0] = MSG_NORMALIZED_MOUSE;
            Array.Copy(BitConverter.GetBytes(normalizedX), 0, buf, 1, 4);
            Array.Copy(BitConverter.GetBytes(normalizedY), 0, buf, 1 + 4, 4);
            try
            {
                _udpClient.Send(buf, buf.Length, _remoteEndPoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UDP][SendNormalized] Send error: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            _running = false;
            _handshakeComplete = false;
            try { _udpClient?.Close(); } catch { }
            try { _udpClient?.Dispose(); } catch { }
            _udpClient = null;

            try
            {
                if (_recvThread != null && _recvThread.IsAlive)
                {
                    if (!_recvThread.Join(500))
                    {
                        // let the background thread exit naturally; it checks _running
                    }
                }
            }
            catch { }
        }

        // This loop receives messages and dispatches based on message type
        private void ReceiveMouseLoopUDP()
        {
            Console.WriteLine("[UDP][RecvLoop] Enter");
            try
            {
                if (_udpClient == null) return;
                var remoteEP = new IPEndPoint(IPAddress.Any, 0);
                var lastHeartbeat = DateTime.UtcNow;

                while (_running)
                {
                    try
                    {
                        var data = _udpClient.Receive(ref remoteEP);
                        _recvLoopLastAlive = DateTime.UtcNow;
                        Interlocked.Increment(ref _recvLoopIterations);

                        if ((DateTime.UtcNow - lastHeartbeat) >= TimeSpan.FromSeconds(5))
                        {
                            var iters = Interlocked.Read(ref _recvLoopIterations);
                            Console.WriteLine($"[UDP][RecvLoop] alive. iterations={iters}, lastAlive={_recvLoopLastAlive:O}");
                            lastHeartbeat = DateTime.UtcNow;
                        }

                        if (data == null || data.Length == 0) continue;

                        // Update remote endpoint if not set (for host mode)
                        if (_remoteEndPoint == null)
                        {
                            _remoteEndPoint = new IPEndPoint(remoteEP.Address, UdpPort);
                            Console.WriteLine($"[UDP] Remote endpoint set to {_remoteEndPoint.Address}:{_remoteEndPoint.Port}");
                        }

                        // Dispatch based on message type
                        switch (data[0])
                        {
                            case MSG_HANDSHAKE_REQUEST:
                                HandleHandshakeRequest(data);
                                break;

                            case MSG_HANDSHAKE_ACCEPT:
                                HandleHandshakeAccept(data);
                                break;

                            case MSG_ROLE_SWITCH_REQUEST:
                                HandleRoleSwitchRequest();
                                break;

                            case MSG_ROLE_SWITCH_ACCEPT:
                                HandleRoleSwitchAccept();
                                break;

                            case MSG_NORMALIZED_MOUSE:
                                // Only process if we are the receiver
                                lock (_roleLock)
                                {
                                    if (!_handshakeComplete || _currentRole != ConnectionRole.Receiver)
                                        break;
                                }

                                if (data.Length >= 1 + 8)
                                {
                                    var nx = BitConverter.ToSingle(data, 1);
                                    var ny = BitConverter.ToSingle(data, 1 + 4);

                                    // Log receiver virtual bounds and values
                                    CoordinateNormalizer.GetVirtualScreenBounds(out var left, out var top, out var width, out var height);
                                    Console.WriteLine($"[UDP][RecvNormalized] from {remoteEP.Address}:{remoteEP.Port} nx={nx:F6}, ny={ny:F6} virtualBounds=[{left},{top},{width},{height}]");

                                    CoordinateNormalizer.NormalizedToScreen(nx, ny, out var sx, out var sy);
                                    Console.WriteLine($"[UDP][RecvNormalized] mapped -> ({sx},{sy})");

                                    // Prevent feedback loop: suppress the very next local move from this exact position
                                    InputHooks.SuppressNextMoveFrom(sx, sy);
                                    SetCursorPos(sx, sy);
                                }
                                break;

                            case MSG_LEGACY_MOUSE:
                                // Only process if we are the receiver
                                lock (_roleLock)
                                {
                                    if (!_handshakeComplete || _currentRole != ConnectionRole.Receiver)
                                        break;
                                }

                                if (data.Length >= 1 + 8)
                                {
                                    var x = BitConverter.ToInt32(data, 1);
                                    var y = BitConverter.ToInt32(data, 1 + 4);
                                    Console.WriteLine($"[UDP][RecvLegacy] from {remoteEP.Address}:{remoteEP.Port} -> ({x},{y})");
                                    InputHooks.SuppressNextMoveFrom(x, y);
                                    SetCursorPos(x, y);
                                }
                                break;

                            default:
                                // Fallback for legacy packets without type prefix
                                if (data.Length == 8)
                                {
                                    var nx = BitConverter.ToSingle(data, 0);
                                    var ny = BitConverter.ToSingle(data, 4);
                                    if (nx >= 0f && nx <= 1f && ny >= 0f && ny <= 1f)
                                    {
                                        CoordinateNormalizer.NormalizedToScreen(nx, ny, out var sx, out var sy);
                                        Console.WriteLine($"[UDP][RecvFallbackFloat] nx={nx:F6}, ny={ny:F6} -> ({sx},{sy})");
                                        InputHooks.SuppressNextMoveFrom(sx, sy);
                                        SetCursorPos(sx, sy);
                                    }
                                    else
                                    {
                                        var ix = BitConverter.ToInt32(data, 0);
                                        var iy = BitConverter.ToInt32(data, 4);
                                        Console.WriteLine($"[UDP][RecvFallbackInt] -> ({ix},{iy})");
                                        InputHooks.SuppressNextMoveFrom(ix, iy);
                                        SetCursorPos(ix, iy);
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"[UDP][Receive] Unknown packet type 0x{data[0]:X2}, length {data.Length} from {remoteEP.Address}:{remoteEP.Port}");
                                }
                                break;
                        }
                    }
                    catch (ThreadAbortException)
                    {
                        Console.WriteLine("[UDP][RecvLoop] ThreadAbortException");
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (!_running) break;
                        Console.WriteLine($"[UDP][Receive] Exception: {ex.Message}");
                        Thread.Sleep(1);
                    }
                }
            }
            finally
            {
                Console.WriteLine("[UDP][RecvLoop] Exit");
            }
        }
























        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        private static void NativeSetCursorPos(int X, int Y) => SetCursorPos(X, Y);

        // Optional: quick state dump you can call from the UI
        public void LogDiagnostics()
        {
            var iters = Interlocked.Read(ref _recvLoopIterations);
            Console.WriteLine($"[UDP][Diag] running={_running}, role={_currentRole}, handshakeComplete={_handshakeComplete}, remoteEP={_remoteEndPoint?.ToString() ?? "<null>"}, iterations={iters}, lastAlive={_recvLoopLastAlive:O}");
        }
    }

    // Enum to represent connection roles
    public enum ConnectionRole : byte
    {
        Receiver = 0,
        Sender = 1
    }
}