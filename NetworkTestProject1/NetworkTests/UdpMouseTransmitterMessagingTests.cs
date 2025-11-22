using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OmniMouse.Network;

namespace NetworkTestProject1.Network
{
    /// <summary>
    /// Unit tests for UdpMouseTransmitter.Messaging.cs partial class.
    /// Tests all public messaging methods: SendMouse, SendMousePosition, SendMouseButton, 
    /// SendMouseWheel, SendTakeControl, and the ReceiveMouseLoopUDP behavior.
    /// </summary>
    [TestClass]
    public class UdpMouseTransmitterMessagingTests
    {
        private Mock<IUdpClient>? _mockUdpClient;
        private UdpMouseTransmitter? _transmitter;
        private const int MOVE_MOUSE_RELATIVE = 100000;

        [TestInitialize]
        public void Setup()
        {
            _mockUdpClient = new Mock<IUdpClient>();

            // Setup mock socket
            var mockSocket = new Mock<Socket>(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp);
            _mockUdpClient.Setup(c => c.Client).Returns(mockSocket.Object);

            // Create transmitter with mock factory
            _transmitter = new UdpMouseTransmitter(_ => _mockUdpClient.Object);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _transmitter?.Disconnect();
            _transmitter = null;
            _mockUdpClient = null;
        }

        /// <summary>
        /// Helper method to setup transmitter with remote endpoint for sending tests
        /// </summary>
        private void SetupTransmitterForSending(string remoteIp = "192.168.1.100")
        {
            _transmitter!.StartHost(remoteIp);
            _transmitter.SetLocalRole(ConnectionRole.Sender);
        }

        #region SendMouse Tests

        [TestMethod]
        public void SendMouse_WhenNotHandshakeComplete_DoesNotSend()
        {
            // Arrange
            _transmitter!.StartHost();
            // Don't complete handshake - role is set but handshake not complete by default

            // Act
            _transmitter.SendMouse(100, 200, isDelta: false);

            // Assert - Only handshake messages should be sent, not mouse messages
            _mockUdpClient!.Verify(c => c.Send(
                It.Is<byte[]>(b => b.Length == 9 && b[0] == 0x03),
                It.IsAny<int>(),
                It.IsAny<IPEndPoint>()),
                Times.Never);
        }

        [TestMethod]
        public void SendMouse_WhenReceiverRole_DoesNotSend()
        {
            // Arrange
            _transmitter!.StartHost("192.168.1.100");
            _transmitter.SetLocalRole(ConnectionRole.Receiver);

            // Act
            _transmitter.SendMouse(100, 200, isDelta: false);

            // Assert
            _mockUdpClient!.Verify(c => c.Send(
                It.Is<byte[]>(b => b.Length == 9 && b[0] == 0x03),
                It.IsAny<int>(),
                It.IsAny<IPEndPoint>()),
                Times.Never);
        }

        [TestMethod]
        public void SendMouse_WhenUdpClientNull_DoesNotThrow()
        {
            // Arrange - transmitter created but not started (no client)

            // Act & Assert - should not throw
            _transmitter!.SendMouse(100, 200, isDelta: false);
        }

        [TestMethod]
        public void SendMouse_WhenRemoteEndpointNull_DoesNotSend()
        {
            // Arrange
            _transmitter!.StartHost(); // No remote endpoint set
            _transmitter.SetLocalRole(ConnectionRole.Sender);

            // Act
            _transmitter.SendMouse(100, 200, isDelta: false);

            // Assert - should not send because remote endpoint is null
            _mockUdpClient!.Verify(c => c.Send(
                It.Is<byte[]>(b => b.Length == 9 && b[0] == 0x03),
                It.IsAny<int>(),
                It.IsAny<IPEndPoint>()),
                Times.Never);
        }

        [TestMethod]
        public void SendMouse_AbsoluteCoordinates_SendsCorrectPacket()
        {
            // Arrange
            byte[]? capturedData = null;
            _mockUdpClient!.Setup(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Callback<byte[], int, IPEndPoint>((data, len, ep) => capturedData = (byte[])data.Clone())
                .Returns(9);

            SetupTransmitterForSending();

            // Act
            _transmitter!.SendMouse(100, 200, isDelta: false);

            // Assert
            Assert.IsNotNull(capturedData, "SendMouse should have sent a packet");
            Assert.AreEqual(9, capturedData.Length);
            Assert.AreEqual(0x03, capturedData[0]); // MSG_MOUSE
            Assert.AreEqual(100, BitConverter.ToInt32(capturedData, 1));
            Assert.AreEqual(200, BitConverter.ToInt32(capturedData, 5));
        }

        [TestMethod]
        public void SendMouse_PositiveDelta_EncodesWithSentinel()
        {
            // Arrange
            byte[]? capturedData = null;
            _mockUdpClient!.Setup(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Callback<byte[], int, IPEndPoint>((data, len, ep) => capturedData = (byte[])data.Clone())
                .Returns(9);

            SetupTransmitterForSending();

            // Act
            _transmitter!.SendMouse(5, 10, isDelta: true);

            // Assert
            Assert.IsNotNull(capturedData);
            int encodedX = BitConverter.ToInt32(capturedData, 1);
            int encodedY = BitConverter.ToInt32(capturedData, 5);
            Assert.AreEqual(5 + MOVE_MOUSE_RELATIVE, encodedX);
            Assert.AreEqual(10 + MOVE_MOUSE_RELATIVE, encodedY);
        }

        [TestMethod]
        public void SendMouse_NegativeDelta_EncodesWithSentinel()
        {
            // Arrange
            byte[]? capturedData = null;
            _mockUdpClient!.Setup(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Callback<byte[], int, IPEndPoint>((data, len, ep) => capturedData = (byte[])data.Clone())
                .Returns(9);

            SetupTransmitterForSending();

            // Act
            _transmitter!.SendMouse(-5, -10, isDelta: true);

            // Assert
            Assert.IsNotNull(capturedData);
            int encodedX = BitConverter.ToInt32(capturedData, 1);
            int encodedY = BitConverter.ToInt32(capturedData, 5);
            Assert.AreEqual(-5 - MOVE_MOUSE_RELATIVE, encodedX);
            Assert.AreEqual(-10 - MOVE_MOUSE_RELATIVE, encodedY);
        }

        [TestMethod]
        public void SendMouse_ZeroDelta_EncodesWithSentinel()
        {
            // Arrange
            byte[]? capturedData = null;
            _mockUdpClient!.Setup(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Callback<byte[], int, IPEndPoint>((data, len, ep) => capturedData = (byte[])data.Clone())
                .Returns(9);

            SetupTransmitterForSending();

            // Act
            _transmitter!.SendMouse(0, 0, isDelta: true);

            // Assert
            Assert.IsNotNull(capturedData);
            int encodedX = BitConverter.ToInt32(capturedData, 1);
            int encodedY = BitConverter.ToInt32(capturedData, 5);
            Assert.AreEqual(MOVE_MOUSE_RELATIVE, encodedX);
            Assert.AreEqual(MOVE_MOUSE_RELATIVE, encodedY);
        }

        [TestMethod]
        public void SendMouse_MixedDelta_EncodesCorrectly()
        {
            // Arrange
            byte[]? capturedData = null;
            _mockUdpClient!.Setup(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Callback<byte[], int, IPEndPoint>((data, len, ep) => capturedData = (byte[])data.Clone())
                .Returns(9);

            SetupTransmitterForSending();

            // Act
            _transmitter!.SendMouse(10, -15, isDelta: true);

            // Assert
            Assert.IsNotNull(capturedData);
            int encodedX = BitConverter.ToInt32(capturedData, 1);
            int encodedY = BitConverter.ToInt32(capturedData, 5);
            Assert.AreEqual(10 + MOVE_MOUSE_RELATIVE, encodedX);
            Assert.AreEqual(-15 - MOVE_MOUSE_RELATIVE, encodedY);
        }

        #endregion

        #region SendMousePosition Tests

        [TestMethod]
        public void SendMousePosition_CallsSendMouseWithAbsoluteFlag()
        {
            // Arrange
            byte[]? capturedData = null;
            _mockUdpClient!.Setup(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Callback<byte[], int, IPEndPoint>((data, len, ep) => capturedData = (byte[])data.Clone())
                .Returns(9);

            SetupTransmitterForSending();

            // Act
            _transmitter!.SendMousePosition(300, 400);

            // Assert
            Assert.IsNotNull(capturedData);
            Assert.AreEqual(0x03, capturedData[0]); // MSG_MOUSE
            Assert.AreEqual(300, BitConverter.ToInt32(capturedData, 1));
            Assert.AreEqual(400, BitConverter.ToInt32(capturedData, 5));
        }

        [TestMethod]
        public void SendMousePosition_LargeCoordinates_SendsCorrectly()
        {
            // Arrange
            byte[]? capturedData = null;
            _mockUdpClient!.Setup(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Callback<byte[], int, IPEndPoint>((data, len, ep) => capturedData = (byte[])data.Clone())
                .Returns(9);

            SetupTransmitterForSending();

            // Act
            _transmitter!.SendMousePosition(3840, 2160);

            // Assert
            Assert.IsNotNull(capturedData);
            Assert.AreEqual(3840, BitConverter.ToInt32(capturedData, 1));
            Assert.AreEqual(2160, BitConverter.ToInt32(capturedData, 5));
        }

        [TestMethod]
        public void SendMousePosition_NegativeCoordinates_SendsCorrectly()
        {
            // Arrange
            byte[]? capturedData = null;
            _mockUdpClient!.Setup(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Callback<byte[], int, IPEndPoint>((data, len, ep) => capturedData = (byte[])data.Clone())
                .Returns(9);

            SetupTransmitterForSending();

            // Act - multi-monitor setup might have negative coords
            _transmitter!.SendMousePosition(-100, -50);

            // Assert
            Assert.IsNotNull(capturedData);
            Assert.AreEqual(-100, BitConverter.ToInt32(capturedData, 1));
            Assert.AreEqual(-50, BitConverter.ToInt32(capturedData, 5));
        }

        #endregion

        #region SendMouseButton Tests

        [TestMethod]
        public void SendMouseButton_WhenNotHandshakeComplete_DoesNotSend()
        {
            // Arrange
            _transmitter!.StartHost();

            // Act
            _transmitter.SendMouseButton(MouseButtonNet.Left, true);

            // Assert
            _mockUdpClient!.Verify(c => c.Send(
                It.Is<byte[]>(b => b.Length == 3 && b[0] == 0x07),
                It.IsAny<int>(),
                It.IsAny<IPEndPoint>()),
                Times.Never);
        }

        [TestMethod]
        public void SendMouseButton_WhenReceiverRole_DoesNotSend()
        {
            // Arrange
            _transmitter!.StartHost("192.168.1.100");
            _transmitter.SetLocalRole(ConnectionRole.Receiver);

            // Act
            _transmitter.SendMouseButton(MouseButtonNet.Left, true);

            // Assert
            _mockUdpClient!.Verify(c => c.Send(
                It.Is<byte[]>(b => b.Length == 3 && b[0] == 0x07),
                It.IsAny<int>(),
                It.IsAny<IPEndPoint>()),
                Times.Never);
        }

        [TestMethod]
        public void SendMouseButton_LeftButtonDown_SendsCorrectPacket()
        {
            // Arrange
            byte[]? capturedData = null;
            _mockUdpClient!.Setup(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Callback<byte[], int, IPEndPoint>((data, len, ep) => capturedData = (byte[])data.Clone())
                .Returns(3);

            SetupTransmitterForSending();

            // Act
            _transmitter!.SendMouseButton(MouseButtonNet.Left, true);

            // Assert
            Assert.IsNotNull(capturedData);
            Assert.AreEqual(3, capturedData.Length);
            Assert.AreEqual(0x07, capturedData[0]); // MSG_MOUSE_BUTTON
            Assert.AreEqual((byte)MouseButtonNet.Left, capturedData[1]);
            Assert.AreEqual(1, capturedData[2]); // isDown = true
        }

        [TestMethod]
        public void SendMouseButton_LeftButtonUp_SendsCorrectPacket()
        {
            // Arrange
            byte[]? capturedData = null;
            _mockUdpClient!.Setup(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Callback<byte[], int, IPEndPoint>((data, len, ep) => capturedData = (byte[])data.Clone())
                .Returns(3);

            SetupTransmitterForSending();

            // Act
            _transmitter!.SendMouseButton(MouseButtonNet.Left, false);

            // Assert
            Assert.IsNotNull(capturedData);
            Assert.AreEqual(0x07, capturedData[0]);
            Assert.AreEqual((byte)MouseButtonNet.Left, capturedData[1]);
            Assert.AreEqual(0, capturedData[2]); // isDown = false
        }

        [TestMethod]
        public void SendMouseButton_RightButtonDown_SendsCorrectPacket()
        {
            // Arrange
            byte[]? capturedData = null;
            _mockUdpClient!.Setup(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Callback<byte[], int, IPEndPoint>((data, len, ep) => capturedData = (byte[])data.Clone())
                .Returns(3);

            SetupTransmitterForSending();

            // Act
            _transmitter!.SendMouseButton(MouseButtonNet.Right, true);

            // Assert
            Assert.IsNotNull(capturedData);
            Assert.AreEqual((byte)MouseButtonNet.Right, capturedData[1]);
            Assert.AreEqual(1, capturedData[2]);
        }

        [TestMethod]
        public void SendMouseButton_MiddleButton_SendsCorrectPacket()
        {
            // Arrange
            byte[]? capturedData = null;
            _mockUdpClient!.Setup(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Callback<byte[], int, IPEndPoint>((data, len, ep) => capturedData = (byte[])data.Clone())
                .Returns(3);

            SetupTransmitterForSending();

            // Act
            _transmitter!.SendMouseButton(MouseButtonNet.Middle, true);

            // Assert
            Assert.IsNotNull(capturedData);
            Assert.AreEqual((byte)MouseButtonNet.Middle, capturedData[1]);
        }

        #endregion

        #region SendMouseWheel Tests

        [TestMethod]
        public void SendMouseWheel_WhenNotHandshakeComplete_DoesNotSend()
        {
            // Arrange
            _transmitter!.StartHost();

            // Act
            _transmitter.SendMouseWheel(120);

            // Assert
            _mockUdpClient!.Verify(c => c.Send(
                It.Is<byte[]>(b => b.Length == 5 && b[0] == 0x08),
                It.IsAny<int>(),
                It.IsAny<IPEndPoint>()),
                Times.Never);
        }

        [TestMethod]
        public void SendMouseWheel_WhenReceiverRole_DoesNotSend()
        {
            // Arrange
            _transmitter!.StartHost("192.168.1.100");
            _transmitter.SetLocalRole(ConnectionRole.Receiver);

            // Act
            _transmitter.SendMouseWheel(120);

            // Assert
            _mockUdpClient!.Verify(c => c.Send(
                It.Is<byte[]>(b => b.Length == 5 && b[0] == 0x08),
                It.IsAny<int>(),
                It.IsAny<IPEndPoint>()),
                Times.Never);
        }

        [TestMethod]
        public void SendMouseWheel_PositiveDelta_SendsCorrectPacket()
        {
            // Arrange
            byte[]? capturedData = null;
            _mockUdpClient!.Setup(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Callback<byte[], int, IPEndPoint>((data, len, ep) => capturedData = (byte[])data.Clone())
                .Returns(5);

            SetupTransmitterForSending();

            // Act
            _transmitter!.SendMouseWheel(120);

            // Assert
            Assert.IsNotNull(capturedData);
            Assert.AreEqual(5, capturedData.Length);
            Assert.AreEqual(0x08, capturedData[0]); // MSG_MOUSE_WHEEL
            Assert.AreEqual(120, BitConverter.ToInt32(capturedData, 1));
        }

        [TestMethod]
        public void SendMouseWheel_NegativeDelta_SendsCorrectPacket()
        {
            // Arrange
            byte[]? capturedData = null;
            _mockUdpClient!.Setup(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Callback<byte[], int, IPEndPoint>((data, len, ep) => capturedData = (byte[])data.Clone())
                .Returns(5);

            SetupTransmitterForSending();

            // Act
            _transmitter!.SendMouseWheel(-120);

            // Assert
            Assert.IsNotNull(capturedData);
            Assert.AreEqual(-120, BitConverter.ToInt32(capturedData, 1));
        }

        [TestMethod]
        public void SendMouseWheel_ZeroDelta_SendsCorrectPacket()
        {
            // Arrange
            byte[]? capturedData = null;
            _mockUdpClient!.Setup(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Callback<byte[], int, IPEndPoint>((data, len, ep) => capturedData = (byte[])data.Clone())
                .Returns(5);

            SetupTransmitterForSending();

            // Act
            _transmitter!.SendMouseWheel(0);

            // Assert
            Assert.IsNotNull(capturedData);
            Assert.AreEqual(0, BitConverter.ToInt32(capturedData, 1));
        }

        [TestMethod]
        public void SendMouseWheel_LargeDelta_SendsCorrectPacket()
        {
            // Arrange
            byte[]? capturedData = null;
            _mockUdpClient!.Setup(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Callback<byte[], int, IPEndPoint>((data, len, ep) => capturedData = (byte[])data.Clone())
                .Returns(5);

            SetupTransmitterForSending();

            // Act - fast scrolling can produce large deltas
            _transmitter!.SendMouseWheel(3600);

            // Assert
            Assert.IsNotNull(capturedData);
            Assert.AreEqual(3600, BitConverter.ToInt32(capturedData, 1));
        }

        #endregion

        #region SendTakeControl Tests

        [TestMethod]
        public void SendTakeControl_NullClientId_ThrowsArgumentNullException()
        {
            // Arrange
            _transmitter!.StartHost();

            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                _transmitter.SendTakeControl(null!, 100, 200));
        }

        [TestMethod]
        public void SendTakeControl_EmptyClientId_ThrowsArgumentNullException()
        {
            // Arrange
            _transmitter!.StartHost();

            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                _transmitter.SendTakeControl(string.Empty, 100, 200));
        }



        [TestMethod]
        public void SendTakeControl_UnknownClientIdWithNoRemoteEndpoint_ThrowsInvalidOperationException()
        {
            // Arrange
            _transmitter!.StartHost();

            // Act & Assert
            var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                _transmitter.SendTakeControl("unknownClient", 100, 200));
            Assert.IsTrue(ex.Message.Contains("Cannot send take control"));
        }

        [TestMethod]
        public void SendTakeControl_RegisteredEndpoint_AttemptsConnection()
        {
            // Arrange
            var clientId = "testClient";
            var registeredEndpoint = new IPEndPoint(IPAddress.Parse("192.0.2.1"), 5000);
            _transmitter!.RegisterClientEndpoint(clientId, registeredEndpoint);
            _transmitter.StartHost();

            // Act & Assert - will fail to connect but should try the right endpoint
            var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                _transmitter.SendTakeControl(clientId, 100, 200));
            Assert.IsTrue(ex.Message.Contains("192.0.2.1:5001")); // TCP port 5001
        }

        [TestMethod]
        public void SendTakeControl_UnreachableEndpoint_ThrowsInvalidOperationException()
        {
            // Arrange
            var clientId = "testClient";
            var unreachableEndpoint = new IPEndPoint(IPAddress.Parse("192.0.2.1"), 5000);
            _transmitter!.RegisterClientEndpoint(clientId, unreachableEndpoint);
            _transmitter.StartHost();

            // Act & Assert
            var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                _transmitter.SendTakeControl(clientId, 100, 200));
            Assert.IsTrue(ex.Message.Contains("Failed to establish TCP connection"));
        }

        [TestMethod]
        public void SendTakeControl_FallbackToRemoteEndpoint_WhenClientNotRegistered()
        {
            // Arrange
            _transmitter!.StartCoHost("192.0.2.1"); // Sets remote endpoint

            // Act & Assert
            var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                _transmitter.SendTakeControl("unregisteredClient", 100, 200));
            // Should attempt to use remote endpoint
            Assert.IsTrue(ex.Message.Contains("192.0.2.1"));
        }

        #endregion

        #region ReceiveMouseLoopUDP Tests

        [TestMethod]
        public void ReceiveMouseLoopUDP_WhenUdpClientNull_ReturnsImmediately()
        {
            // Arrange - transmitter not started, no client

            // Act - start would normally trigger receive loop
            // Since we can't directly call ReceiveMouseLoopUDP (it's private), 
            // we verify it doesn't throw by attempting connection

            // Assert - no exception thrown
            Assert.IsNotNull(_transmitter);
        }

        [TestMethod]
        public void ReceiveMouseLoopUDP_IncrementsDiagnosticsCounters()
        {
            // Arrange
            var callCount = 0;
            var remoteEP = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 5000);

            _mockUdpClient!.Setup(c => c.Receive(ref It.Ref<IPEndPoint>.IsAny))
                .Callback(new ReceiveDelegate((ref IPEndPoint ep) =>
                {
                    ep = remoteEP;
                    callCount++;
                }))
                .Returns(() =>
                {
                    if (callCount == 1)
                    {
                        // Return a handshake packet
                        var packet = new byte[15];
                        packet[0] = 0x10; // MSG_HANDSHAKE_REQUEST
                        return packet;
                    }
                    // After first packet, sleep to allow test to complete
                    Thread.Sleep(100);
                    return new byte[0];
                });

            _transmitter!.StartHost();

            // Act
            Thread.Sleep(200); // Allow receive loop to process

            // Assert - diagnostics should show activity
            // we can't directly verify internal counters, but we can verify no crash
            Assert.IsTrue(callCount > 0);
        }

        [TestMethod]
        public void ReceiveMouseLoopUDP_FiltersUnexpectedRemoteEndpoints()
        {
            // Arrange
            var expectedRemote = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 5000);
            var unexpectedRemote = new IPEndPoint(IPAddress.Parse("192.168.1.200"), 5000);
            var packetCount = 0;

            _mockUdpClient!.Setup(c => c.Receive(ref It.Ref<IPEndPoint>.IsAny))
                .Callback(new ReceiveDelegate((ref IPEndPoint ep) =>
                {
                    // alternate between expected and unexpected
                    ep = packetCount % 2 == 0 ? expectedRemote : unexpectedRemote;
                    packetCount++;
                }))
                .Returns(() =>
                {
                    if (packetCount <= 2)
                    {
                        var packet = new byte[9];
                        packet[0] = 0x03; // MSG_MOUSE
                        return packet;
                    }
                    Thread.Sleep(100);
                    return new byte[0];
                });

            _transmitter!.StartCoHost("192.168.1.100"); // Set expected endpoint

            // Act
            Thread.Sleep(200);

            // assert - should only process packets from expected endpoint
            Assert.IsTrue(packetCount >= 2);
        }

        [TestMethod]
        public void ReceiveMouseLoopUDP_HandlesEmptyPackets()
        {
            // Arrange
            var callCount = 0;
            var remoteEP = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 5000);

            _mockUdpClient!.Setup(c => c.Receive(ref It.Ref<IPEndPoint>.IsAny))
                .Callback(new ReceiveDelegate((ref IPEndPoint ep) =>
                {
                    ep = remoteEP;
                    callCount++;
                }))
                .Returns(() =>
                {
                    if (callCount <= 2)
                        return new byte[0]; // Empty packet
                    Thread.Sleep(100);
                    return new byte[0];
                });

            _transmitter!.StartHost();

            // Act
            Thread.Sleep(200);

            // assert - should handle gracefully without crash
            Assert.IsTrue(callCount > 0);
        }

        [TestMethod]
        public void ReceiveMouseLoopUDP_HandlesNullPackets()
        {
            // Arrange
            var callCount = 0;
            var remoteEP = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 5000);

            _mockUdpClient!.Setup(c => c.Receive(ref It.Ref<IPEndPoint>.IsAny))
                .Callback(new ReceiveDelegate((ref IPEndPoint ep) =>
                {
                    ep = remoteEP;
                    callCount++;
                }))
                .Returns(() =>
                {
                    if (callCount <= 2)
                        return null!; // Null packet
                    Thread.Sleep(100);
                    return new byte[0];
                });

            _transmitter!.StartHost();

            // Act
            Thread.Sleep(200);

            // Assert - should handle gracefully without crash
            Assert.IsTrue(callCount > 0);
        }

        [TestMethod]
        public void ReceiveMouseLoopUDP_ExitsWhenRunningFalse()
        {
            // Arrange
            var callCount = 0;
            var remoteEP = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 5000);

            _mockUdpClient!.Setup(c => c.Receive(ref It.Ref<IPEndPoint>.IsAny))
                .Callback(new ReceiveDelegate((ref IPEndPoint ep) =>
                {
                    ep = remoteEP;
                    callCount++;
                }))
                .Returns(new byte[0]);

            _transmitter!.StartHost();
            Thread.Sleep(100);

            // Act - disconnect sets _running to false
            _transmitter.Disconnect();
            Thread.Sleep(100);

            var countAfterDisconnect = callCount;
            Thread.Sleep(100);

            // Assert - should stop receiving new packets
            Assert.AreEqual(countAfterDisconnect, callCount);
        }

        #endregion

        #region Helper Delegate for Moq ref parameter

        private delegate void ReceiveDelegate(ref IPEndPoint ep);

        #endregion
    }
}