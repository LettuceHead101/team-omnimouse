using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MessagePack;
using OmniMouse.Core.Packets;

namespace OmniMouse.Network
{
    public interface IUdpMouseTransmitter
    {
        void StartHost();
        void StartCoHost(string hostIp);
        void SendMousePosition(int x, int y);
    }

    public class UdpMouseTransmitter : IUdpMouseTransmitter
    {
        private UdpClient? _udpClient;
        private IPEndPoint? _remoteEndPoint;
        private bool _isCoHost = false;
        private string _hostIp = "";
        private const int UdpPort = 5000;

        public void StartHost()
        {
            _udpClient = new UdpClient(UdpPort);
            Console.WriteLine($"Listening for UDP packets on port {UdpPort}...");
            new Thread(ReceiveMouseLoopUDP) { IsBackground = true }.Start();
        }

        public void StartCoHost(string hostIp)
        {
            _isCoHost = true;
            _hostIp = hostIp;
            _udpClient = new UdpClient();
            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(hostIp), UdpPort);
            Console.WriteLine($"Sending mouse positions to {hostIp}:{UdpPort} via UDP...");
        }

        public void SendMousePosition(int x, int y)
        {
            if (_isCoHost && _udpClient != null && _remoteEndPoint != null)
            {
                var packet = new OmniMouse.Core.Packets.MousePacket { X = x, Y = y };
                var msg = MessagePackSerializer.Serialize(packet);
                try { _udpClient.Send(msg, msg.Length, _remoteEndPoint); } catch { }
            }
        }

        private void ReceiveMouseLoopUDP()
        {
            var ep = new IPEndPoint(IPAddress.Any, UdpPort);
            while (true)
            {
                try
                {
                    if (_udpClient != null)
                    {
                        var data = _udpClient.Receive(ref ep);
                        var packet = MessagePackSerializer.Deserialize<OmniMouse.Core.Packets.MousePacket>(data);
                        SetCursorPos(packet.X, packet.Y);
                    }
                }
                catch { Thread.Sleep(10); }
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);
    }
}