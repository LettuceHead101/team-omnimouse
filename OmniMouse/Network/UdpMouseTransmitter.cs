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
            try
            {
                var client = new UdpClient(UdpPort);
                // allow reuse (optional)
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient = client;
                Console.WriteLine($"[UDP] Listening for UDP packets on port {UdpPort}...");
                new Thread(ReceiveMouseLoopUDP) { IsBackground = true }.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UDP][StartHost] Failed to start listener: {ex.Message}");
                throw;
            }
        }

        public void StartCoHost(string hostIp)
        {
            try
            {
                _isCoHost = true;
                _hostIp = hostIp;
                _udpClient = new UdpClient(); // ephemeral local port
                _remoteEndPoint = new IPEndPoint(IPAddress.Parse(hostIp), UdpPort);
                Console.WriteLine($"[UDP] Sending mouse positions to {_remoteEndPoint.Address}:{_remoteEndPoint.Port} via UDP...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UDP][StartCoHost] Failed to configure sender: {ex.Message}");
                throw;
            }
        }

        public void SendMousePosition(int x, int y)
        {
            if (_isCoHost && _udpClient != null && _remoteEndPoint != null)
            {
                try
                {
                    var packet = new MousePacket { X = x, Y = y };
                    var msg = MessagePackSerializer.Serialize(packet);
                    int sent = _udpClient.Send(msg, msg.Length, _remoteEndPoint);
                    Console.WriteLine($"[UDP][Send] Sent {sent} bytes to {_remoteEndPoint.Address}:{_remoteEndPoint.Port} -> ({x},{y})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UDP][Send] Error sending packet: {ex.Message}");
                }
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
                        Console.WriteLine($"[UDP][Receive] Received {data.Length} bytes from {ep.Address}:{ep.Port}");
                        try
                        {
                            var packet = MessagePackSerializer.Deserialize<MousePacket>(data);
                            Console.WriteLine($"[UDP][Receive] Moving cursor to ({packet.X},{packet.Y})");
                            SetCursorPos(packet.X, packet.Y);
                        }
                        catch (Exception exInner)
                        {
                            Console.WriteLine($"[UDP][Receive] Failed to deserialize or apply packet: {exInner.Message}");
                        }
                    }
                }
                catch (SocketException sockEx)
                {
                    Console.WriteLine($"[UDP][Receive] SocketException: {sockEx.Message}");
                    Thread.Sleep(100);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UDP][Receive] Exception: {ex.Message}");
                    Thread.Sleep(100);
                }
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);
    }
}