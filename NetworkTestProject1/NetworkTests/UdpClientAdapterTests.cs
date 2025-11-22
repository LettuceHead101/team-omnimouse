using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.Network;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkTestProject1.Network
{
    [TestClass]
    public class UdpClientAdapterTests
    {
        // Use SemaphoreSlim for asynchronous locking to prevent network conflicts.
        private static readonly SemaphoreSlim _networkSemaphore = new SemaphoreSlim(1, 1);

        [TestMethod]
        public async Task Constructor_BindsToSpecifiedPort()
        {
            await _networkSemaphore.WaitAsync();
            try
            {
                // Arrange & Act
                // Use port 0 to let the OS assign an available ephemeral port.
                using var client = new UdpClientAdapter(0);

                // Assert
                Assert.IsNotNull(client.Client);
                Assert.IsTrue(client.Client.IsBound);
                var localEndPoint = client.Client.LocalEndPoint as IPEndPoint;
                Assert.IsNotNull(localEndPoint);
                Assert.IsTrue(localEndPoint.Port > 0, "Port should be assigned by the OS.");
            }
            finally
            {
                _networkSemaphore.Release();
            }
        }

        [TestMethod]
        public async Task Close_DisposesSocket()
        {
            await _networkSemaphore.WaitAsync();
            try
            {
                // Arrange
                var client = new UdpClientAdapter(0); // Use ephemeral port
                var socket = client.Client;

                // Act
                client.Close();

                // Assert
                Assert.ThrowsException<ObjectDisposedException>(() => socket.LocalEndPoint);
            }
            finally
            {
                _networkSemaphore.Release();
            }
        }

        [TestMethod]
        public async Task SendAndReceive_TransmitsDataCorrectly()
        {
            await _networkSemaphore.WaitAsync();
            try
            {
                // Arrange
                using var server = new UdpClientAdapter(0); // Bind to any available port
                using var client = new UdpClientAdapter(0); // Bind to any available port

                var serverListenEndPoint = (IPEndPoint)server.Client.LocalEndPoint!;
                // When sending locally, target the loopback address with the server's port.
                var serverTargetEndPoint = new IPEndPoint(IPAddress.Loopback, serverListenEndPoint.Port);
                var clientEndPoint = (IPEndPoint)client.Client.LocalEndPoint!;

                var message = "Hello, UDP!";
                var sendData = Encoding.UTF8.GetBytes(message);

                IPEndPoint receiveEndPoint = new IPEndPoint(IPAddress.Any, 0);

                // Act
                // Start the receiver task first.
                var receiveTask = Task.Run(() => server.Receive(ref receiveEndPoint));

                // Give the receiver a moment to enter the blocking Receive call.
                await Task.Delay(100);

                // Now, send the data to the correct loopback endpoint.
                client.Send(sendData, sendData.Length, serverTargetEndPoint);

                // Wait for the receive task to complete.
                var receivedData = await receiveTask;
                var receivedMessage = Encoding.UTF8.GetString(receivedData);

                // Assert
                Assert.AreEqual(message, receivedMessage, "The received message should match the sent message.");

                // The client's local endpoint may be bound to IPAddress.Any (0.0.0.0).
                // In that case the actual sender address observed by the receiver
                // will be the loopback address (127.0.0.1) when sending locally.
                if (clientEndPoint.Address.Equals(IPAddress.Any))
                {
                    Assert.AreEqual(IPAddress.Loopback, receiveEndPoint.Address, "The sender's IP address should be loopback when client bound to Any.");
                }
                else
                {
                    Assert.AreEqual(clientEndPoint.Address, receiveEndPoint.Address, "The sender's IP address should be correct.");
                }

                Assert.AreEqual(clientEndPoint.Port, receiveEndPoint.Port, "The sender's port should be correct.");
            }
            finally
            {
                _networkSemaphore.Release();
            }
        }

        [TestMethod]
        public async Task Send_ToNullEndpoint_ThrowsSocketException()
        {
            await _networkSemaphore.WaitAsync();
            try
            {
                // Arrange
                using var client = new UdpClientAdapter(0); // Use ephemeral port
                var data = new byte[] { 1, 2, 3 };

                // Act & Assert
                // The underlying UdpClient throws a SocketException when the endpoint is null.
                Assert.ThrowsException<SocketException>(() => client.Send(data, data.Length, null));
            }
            finally
            {
                _networkSemaphore.Release();
            }
        }
    }
}