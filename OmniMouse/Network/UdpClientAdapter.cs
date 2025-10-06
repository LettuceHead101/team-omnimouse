using System.Net;
using System.Net.Sockets;

namespace OmniMouse.Network
{
    public class UdpClientAdapter : IUdpClient
    {
        private readonly UdpClient _udpClient;

        public UdpClientAdapter(int port)
        {
            _udpClient = new UdpClient(port);
        }

        public UdpClientAdapter()
        {
            _udpClient = new UdpClient();
        }

        public Socket Client => _udpClient.Client;

        public void Close() => _udpClient.Close();

        public void Dispose() => _udpClient.Dispose(); // Dispose() method to act as the master cleanup routine. It ensures all resources, including the underlying socket are properly released.

        public byte[] Receive(ref IPEndPoint remoteEP) => _udpClient.Receive(ref remoteEP);

        public int Send(byte[] dgram, int bytes, IPEndPoint? endPoint) => _udpClient.Send(dgram, bytes, endPoint);
    }
}