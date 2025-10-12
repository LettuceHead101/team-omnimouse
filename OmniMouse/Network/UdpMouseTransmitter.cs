using System;
using System.Net;
using System.Threading;
using System.Runtime.InteropServices;

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
        private Thread? _recvThread;
        private volatile bool _running;

        public UdpMouseTransmitter() : this(() => new UdpClientAdapter(), port => new UdpClientAdapter(port)) { }

        public UdpMouseTransmitter(Func<IUdpClient> udpClientFactory, Func<int, IUdpClient> udpClientFactoryWithPort)
        {
            _udpClientFactory = udpClientFactory ?? throw new ArgumentNullException(nameof(udpClientFactory));
            _udpClientFactoryWithPort = udpClientFactoryWithPort ?? throw new ArgumentNullException(nameof(udpClientFactoryWithPort));
        }

        public void StartHost()
        {
            _isCoHost = false;
            // bind receiver to port
            _udpClient = _udpClientFactoryWithPort(UdpPort);
            StartReceiveLoop();
        }

        public void StartCoHost(string hostIp)
        {
            _isCoHost = true;
            _hostIp = hostIp;
            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(hostIp), UdpPort);
            _udpClient = _udpClientFactory(); // local socket for client sends
            StartReceiveLoop();
        }

        private void StartReceiveLoop()
        {
            _running = true;
            _recvThread = new Thread(ReceiveMouseLoopUDP) { IsBackground = true };
            _recvThread.Start();
        }

        // Send raw pixel ints (legacy)
        public void SendMousePosition(int x, int y)
        {
            if (_udpClient == null) return;
            if (_isCoHost && _remoteEndPoint != null)
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
            if (_udpClient == null || !_isCoHost || _remoteEndPoint == null) return;
            var buf = new byte[1 + 4 + 4];
            buf[0] = 0x01;
            Array.Copy(BitConverter.GetBytes(normalizedX), 0, buf, 1, 4);
            Array.Copy(BitConverter.GetBytes(normalizedY), 0, buf, 1 + 4, 4);
            _udpClient.Send(buf, buf.Length, _remoteEndPoint);
        }

        public void Disconnect()
        {
            _running = false;
            try
            {
                _udpClient?.Close();
            }
            catch { }
            _udpClient?.Dispose();
            _udpClient = null;
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
                        CoordinateNormalizer.NormalizedToScreen(nx, ny, out var sx, out var sy);
                        SetCursorPos(sx, sy);
                    }
                    else if (data[0] == 0x02 && data.Length >= 1 + 8)
                    {
                        var x = BitConverter.ToInt32(data, 1);
                        var y = BitConverter.ToInt32(data, 1 + 4);
                        SetCursorPos(x, y);
                    }
                    else
                    {
                        // try fallback: if message is exactly 8 bytes, maybe it's two ints or two floats without header.
                        if (data.Length == 8)
                        {
                            // try floats first
                            var nx = BitConverter.ToSingle(data, 0);
                            var ny = BitConverter.ToSingle(data, 4);
                            // If values in 0..1, treat as normalized
                            if (nx >= 0f && nx <= 1f && ny >= 0f && ny <= 1f)
                            {
                                CoordinateNormalizer.NormalizedToScreen(nx, ny, out var sx, out var sy);
                                SetCursorPos(sx, sy);
                            }
                            else
                            {
                                // treat as two ints
                                var ix = BitConverter.ToInt32(data, 0);
                                var iy = BitConverter.ToInt32(data, 4);
                                SetCursorPos(ix, iy);
                            }
                        }
                    }
                }
                catch (ThreadAbortException) { break; }
                catch (Exception) { Thread.Sleep(1); /* ignore transient errors */ }
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);
    }
}