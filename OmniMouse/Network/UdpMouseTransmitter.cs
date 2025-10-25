using System;
using System.Net;
using System.Threading;
using System.Runtime.InteropServices;
using OmniMouse.Hooks; // for feedback-loop suppression

namespace OmniMouse.Network
{
    public class UdpMouseTransmitter : IUdpMouseTransmitter
    {
        private IUdpClient? _udpClient;
        private IPEndPoint? _remoteEndPoint;
        private bool _isCoHost = false;
        private string _hostIp = "";
        private const int UdpPort = 5000;
        private readonly Func<int, IUdpClient> _udpClientFactoryWithPort;
        private readonly Func<IUdpClient> _udpClientFactory;
        private readonly Action<int, int> _setCursorPos;
        private Thread? _recvThread;
        private volatile bool _running;

        public UdpMouseTransmitter()
            : this(() => new UdpClientAdapter(), port => new UdpClientAdapter(port), NativeSetCursorPos) { }

        public UdpMouseTransmitter(Func<IUdpClient> udpClientFactory, Func<int, IUdpClient> udpClientFactoryWithPort)
            : this(udpClientFactory, udpClientFactoryWithPort, NativeSetCursorPos) { }

        public UdpMouseTransmitter(Func<IUdpClient> udpClientFactory,
                                   Func<int, IUdpClient> udpClientFactoryWithPort,
                                   Action<int, int> setCursorPos)
        {
            _udpClientFactory = udpClientFactory ?? throw new ArgumentNullException(nameof(udpClientFactory));
            _udpClientFactoryWithPort = udpClientFactoryWithPort ?? throw new ArgumentNullException(nameof(udpClientFactoryWithPort));
            _setCursorPos = setCursorPos ?? NativeSetCursorPos;
        }

        public void StartHost()
        {
            _isCoHost = false;
            _udpClient = _udpClientFactoryWithPort(UdpPort);
            Console.WriteLine($"[UDP] Host listening on {UdpPort}.");
            StartReceiveLoop();
        }

        public void StartCoHost(string hostIp)
        {
            _isCoHost = true;
            _hostIp = hostIp;
            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(hostIp), UdpPort);
            _udpClient = _udpClientFactoryWithPort(0);
            Console.WriteLine($"[UDP] CoHost sender using local port {_udpClient.Client.LocalEndPoint} -> sending to {_remoteEndPoint.Address}:{_remoteEndPoint.Port}");
            StartReceiveLoop();
        }

        // Symmetric mode: bind to UdpPort and send to peerIp:UdpPort
        public void StartPeer(string peerIp)
        {
            _isCoHost = true; // indicates we will send
            _hostIp = peerIp;
            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(peerIp), UdpPort);

            _udpClient = _udpClientFactoryWithPort(UdpPort); // bind to well-known port for receiving
            Console.WriteLine($"[UDP] Peer mode bound to {UdpPort}, remote {_remoteEndPoint.Address}:{_remoteEndPoint.Port}");
            StartReceiveLoop();
        }

        private void StartReceiveLoop()
        {
            if (_running) return;
            _running = true;
            _recvThread = new Thread(ReceiveMouseLoopUDP) { IsBackground = true };
            _recvThread.Start();
        }

        // Send raw pixel ints (legacy)
        public void SendMousePosition(int x, int y)
        {
            if (_udpClient == null) return;
            if (_remoteEndPoint != null)
            {
                // simple legacy format: prefix 0x02, then two 4-byte ints
                var buf = new byte[1 + 8];
                buf[0] = 0x02;
                Array.Copy(BitConverter.GetBytes(x), 0, buf, 1, 4);
                Array.Copy(BitConverter.GetBytes(y), 0, buf, 1 + 4, 4);
                _udpClient.Send(buf, buf.Length, _remoteEndPoint);
            }
        }

        // NEW: send two normalized floats (0..1). Message type 0x01 then 8 bytes of floats.
        public void SendNormalizedMousePosition(float normalizedX, float normalizedY)
        {
            if (_udpClient == null || _remoteEndPoint == null) return;

            // Log what we're about to send so you can verify sender values
            Console.WriteLine($"[UDP][SendNormalized] -> nx={normalizedX:F6}, ny={normalizedY:F6} to {_remoteEndPoint.Address}:{_remoteEndPoint.Port}");

            var buf = new byte[1 + 4 + 4];
            buf[0] = 0x01;
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

        // This loop receives messages and dispatches to SetCursorPos.
        // Protocol:
        // 0x01 | float32 nx | float32 ny  => normalized coords
        // 0x02 | int32  x | int32  y     => legacy raw pixels
        private void ReceiveMouseLoopUDP()
        {
            if (_udpClient == null) return;
            var remoteEP = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    var data = _udpClient.Receive(ref remoteEP);
                    if (data == null || data.Length == 0) continue;

                    if (data[0] == 0x01 && data.Length >= 1 + 8)
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
                    else if (data[0] == 0x02 && data.Length >= 1 + 8)
                    {
                        var x = BitConverter.ToInt32(data, 1);
                        var y = BitConverter.ToInt32(data, 1 + 4);
                        Console.WriteLine($"[UDP][RecvLegacy] from {remoteEP.Address}:{remoteEP.Port} -> ({x},{y})");
                        InputHooks.SuppressNextMoveFrom(x, y);
                        SetCursorPos(x, y);
                    }
                    else
                    {
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
                            Console.WriteLine($"[UDP][Receive] Unknown packet length {data.Length} from {remoteEP.Address}:{remoteEP.Port}");
                        }
                    }
                }
                catch (ThreadAbortException) { break; }
                catch (Exception ex)
                {
                    if (!_running) break;
                    Console.WriteLine($"[UDP][Receive] Exception: {ex.Message}");
                    Thread.Sleep(1);
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        private static void NativeSetCursorPos(int X, int Y) => SetCursorPos(X, Y);
    }
}