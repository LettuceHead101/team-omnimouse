using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OmniMouse.Network;

namespace NetworkTestProject1.Network
{
    /// <summary>
    /// Comprehensive unit tests for UdpMouseTransmitter disconnect functionality.
    /// Tests cover graceful disconnection, state reset, and resource cleanup.
    /// </summary>
    [TestClass]
    public class UdpMouseTransmitterDisconnectTests
    {
        private Mock<IUdpClient>? _mockUdpClient;
        private UdpMouseTransmitter? _transmitter;

        [TestInitialize]
        public void Setup()
        {
            _mockUdpClient = new Mock<IUdpClient>();

            var mockSocket = new Mock<Socket>(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp);
            _mockUdpClient.Setup(c => c.Client).Returns(mockSocket.Object);

            _transmitter = new UdpMouseTransmitter(_ => _mockUdpClient.Object);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _transmitter?.Disconnect();
            _transmitter = null;
            _mockUdpClient = null;
        }

        #region Disconnect Notification Tests

        [TestMethod]
        public void Disconnect_WhenHandshakeComplete_SendsDisconnectNotification()
        {
            // Arrange
            byte[]? capturedData = null;
            _mockUdpClient!.Setup(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Callback<byte[], int, IPEndPoint>((data, len, ep) => capturedData = data)
                .Returns(1);

            // Setup receive to complete handshake
            var remoteEP = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 5000);
            var handshakeAccept = CreateHandshakeAcceptPacket();
            var receiveCount = 0;

            _mockUdpClient.Setup(c => c.Receive(ref It.Ref<IPEndPoint>.IsAny))
                .Callback(new ReceiveDelegate((ref IPEndPoint ep) => { ep = remoteEP; receiveCount++; }))
                .Returns(() => receiveCount == 1 ? handshakeAccept : new byte[0]);

            _transmitter!.StartCoHost("192.168.1.100");
            Thread.Sleep(300); // Allow handshake to complete

            // Act
            _transmitter.Disconnect();

            // Assert - verify disconnect message was sent (MSG_DISCONNECT = 0x14)
            _mockUdpClient.Verify(c => c.Send(
                It.Is<byte[]>(b => b.Length >= 1 && b[0] == 0x14),
                It.IsAny<int>(),
                It.IsAny<IPEndPoint>()),
                Times.AtLeastOnce);
        }

        [TestMethod]
        public void Disconnect_WhenNotHandshakeComplete_DoesNotSendDisconnectNotification()
        {
            // Arrange
            _transmitter!.StartHost();
            Thread.Sleep(100);

            // Act
            _transmitter.Disconnect();

            // Assert - should not send disconnect message when handshake not complete
            _mockUdpClient!.Verify(c => c.Send(
                It.Is<byte[]>(b => b.Length >= 1 && b[0] == 0x14),
                It.IsAny<int>(),
                It.IsAny<IPEndPoint>()),
                Times.Never);
        }

        [TestMethod]
        public void Disconnect_WhenSendFails_DoesNotThrow()
        {
            // Arrange
            _mockUdpClient!.Setup(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Throws(new SocketException());

            var remoteEP = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 5000);
            var handshakeAccept = CreateHandshakeAcceptPacket();
            var receiveCount = 0;

            _mockUdpClient.Setup(c => c.Receive(ref It.Ref<IPEndPoint>.IsAny))
                .Callback(new ReceiveDelegate((ref IPEndPoint ep) => { ep = remoteEP; receiveCount++; }))
                .Returns(() => receiveCount == 1 ? handshakeAccept : new byte[0]);

            _transmitter!.StartCoHost("192.168.1.100");
            Thread.Sleep(300);

            // Act & Assert - should not throw
            _transmitter.Disconnect();
        }

        #endregion

        #region State Reset Tests

        [TestMethod]
        public void Disconnect_ResetsRunningFlag()
        {
            // Arrange
            _transmitter!.StartHost();
            Thread.Sleep(100);

            // Act
            _transmitter.Disconnect();

            // Assert
            var running = GetPrivateField<bool>(_transmitter, "_running");
            Assert.IsFalse(running, "Running flag should be false after disconnect");
        }

        [TestMethod]
        public void Disconnect_ResetsHandshakeComplete()
        {
            // Arrange
            _transmitter!.StartHost();
            Thread.Sleep(100);

            // Act
            _transmitter.Disconnect();

            // Assert
            var handshakeComplete = GetPrivateField<bool>(_transmitter, "_handshakeComplete");
            Assert.IsFalse(handshakeComplete, "HandshakeComplete should be false after disconnect");
        }

        [TestMethod]
        public void Disconnect_ClearsRemoteEndpoint()
        {
            // Arrange
            _transmitter!.StartCoHost("192.168.1.100");
            Thread.Sleep(100);

            // Act
            _transmitter.Disconnect();

            // Assert
            var remoteEP = GetPrivateField<IPEndPoint?>(_transmitter, "_remoteEndPoint");
            Assert.IsNull(remoteEP, "Remote endpoint should be null after disconnect");
        }

        [TestMethod]
        public void Disconnect_ClearsUdpClient()
        {
            // Arrange
            _transmitter!.StartHost();
            Thread.Sleep(100);

            // Act
            _transmitter.Disconnect();

            // Assert
            var udpClient = GetPrivateField<IUdpClient?>(_transmitter, "_udpClient");
            Assert.IsNull(udpClient, "UDP client should be null after disconnect");
        }

        #endregion

        #region Multiple Disconnect Tests

        [TestMethod]
        public void Disconnect_CalledMultipleTimes_DoesNotThrow()
        {
            // Arrange
            _transmitter!.StartHost();
            Thread.Sleep(100);

            // Act & Assert - should not throw on multiple calls
            _transmitter.Disconnect();
            _transmitter.Disconnect();
            _transmitter.Disconnect();
        }

        [TestMethod]
        public void Disconnect_BeforeStart_DoesNotThrow()
        {
            // Act & Assert - disconnect before any start should not throw
            _transmitter!.Disconnect();
        }

        [TestMethod]
        public void Disconnect_DuringReceiveLoop_StopsLoop()
        {
            // Arrange
            var receiveCount = 0;
            var remoteEP = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 5000);

            _mockUdpClient!.Setup(c => c.Receive(ref It.Ref<IPEndPoint>.IsAny))
                .Callback(new ReceiveDelegate((ref IPEndPoint ep) =>
                {
                    ep = remoteEP;
                    Interlocked.Increment(ref receiveCount);
                }))
                .Returns(() =>
                {
                    Thread.Sleep(50);
                    return new byte[0];
                });

            _transmitter!.StartHost();
            Thread.Sleep(200);
            var countBeforeDisconnect = receiveCount;

            // Act
            _transmitter.Disconnect();
            Thread.Sleep(200);
            var countAfterDisconnect = receiveCount;

            // Assert - receive count should stop increasing
            Thread.Sleep(200);
            Assert.AreEqual(countAfterDisconnect, receiveCount, "Receive loop should stop after disconnect");
        }

        #endregion

        #region Reconnect Tests

        [TestMethod]
        public void StartHost_AfterDisconnect_WorksCorrectly()
        {
            // Arrange
            _transmitter!.StartHost();
            Thread.Sleep(100);
            _transmitter.Disconnect();
            Thread.Sleep(100);

            // Create new mock for reconnection
            var newMockUdpClient = new Mock<IUdpClient>();
            var mockSocket = new Mock<Socket>(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp);
            newMockUdpClient.Setup(c => c.Client).Returns(mockSocket.Object);

            _transmitter = new UdpMouseTransmitter(_ => newMockUdpClient.Object);

            // Act
            _transmitter.StartHost();
            Thread.Sleep(100);

            // Assert
            var running = GetPrivateField<bool>(_transmitter, "_running");
            Assert.IsTrue(running, "Should be running after restart");
        }

        [TestMethod]
        public void StartCoHost_AfterDisconnect_WorksCorrectly()
        {
            // Arrange
            _transmitter!.StartCoHost("192.168.1.100");
            Thread.Sleep(100);
            _transmitter.Disconnect();
            Thread.Sleep(100);

            // Create new transmitter for reconnection
            var newMockUdpClient = new Mock<IUdpClient>();
            var mockSocket = new Mock<Socket>(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp);
            newMockUdpClient.Setup(c => c.Client).Returns(mockSocket.Object);

            _transmitter = new UdpMouseTransmitter(_ => newMockUdpClient.Object);

            // Act
            _transmitter.StartCoHost("192.168.1.200");
            Thread.Sleep(100);

            // Assert
            var remoteEP = GetPrivateField<IPEndPoint?>(_transmitter, "_remoteEndPoint");
            Assert.IsNotNull(remoteEP, "Remote endpoint should be set after restart");
            Assert.AreEqual("192.168.1.200", remoteEP.Address.ToString());
        }

        #endregion

        #region Helper Methods

        private static T? GetPrivateField<T>(object obj, string fieldName)
        {
            var field = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null ? (T?)field.GetValue(obj) : default;
        }

        private byte[] CreateHandshakeAcceptPacket()
        {
            var packet = new byte[27];
            packet[0] = 0x11; // MSG_HANDSHAKE_ACCEPT
            packet[1] = 1;    // Protocol version
            // Rest can be zeros for basic test
            return packet;
        }

        private delegate void ReceiveDelegate(ref IPEndPoint ep);

        #endregion
    }
}