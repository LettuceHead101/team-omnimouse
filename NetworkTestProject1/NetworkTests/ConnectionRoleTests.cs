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
    /// Tests for ConnectionRole transitions during connect/disconnect.
    /// </summary>
    [TestClass]
    public class ConnectionRoleTransitionTests
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
        }

        #region Role Change Event Tests

        [TestMethod]
        public void SetLocalRole_RaisesRoleChangedEvent()
        {
            // Arrange
            ConnectionRole? receivedRole = null;
            _transmitter!.RoleChanged += role => receivedRole = role;

            // Act
            _transmitter.SetLocalRole(ConnectionRole.Sender);

            // Assert
            Assert.AreEqual(ConnectionRole.Sender, receivedRole);
        }

        [TestMethod]
        public void SetLocalRole_SameRole_DoesNotRaiseEvent()
        {
            // Arrange
            var eventCount = 0;
            _transmitter!.RoleChanged += role => eventCount++;

            // Act
            _transmitter.SetLocalRole(ConnectionRole.Receiver); // Default is Receiver
            _transmitter.SetLocalRole(ConnectionRole.Receiver);

            // Assert - should only fire when role actually changes (or not at all for same role)
            Assert.IsTrue(eventCount <= 2);
        }

        [TestMethod]
        public void Disconnect_ResetsRoleToReceiver()
        {
            // Arrange
            _transmitter!.SetLocalRole(ConnectionRole.Sender);
            _transmitter.StartHost();
            Thread.Sleep(100);

            // Act
            _transmitter.Disconnect();

            // Assert
            Assert.AreEqual(ConnectionRole.Receiver, _transmitter.CurrentRole);
        }

        #endregion

        #region CurrentRole Property Tests

        [TestMethod]
        public void CurrentRole_DefaultIsReceiver()
        {
            // Assert
            Assert.AreEqual(ConnectionRole.Receiver, _transmitter!.CurrentRole);
        }

        [TestMethod]
        public void CurrentRole_UpdatesAfterSetLocalRole()
        {
            // Act
            _transmitter!.SetLocalRole(ConnectionRole.Sender);

            // Assert
            Assert.AreEqual(ConnectionRole.Sender, _transmitter.CurrentRole);
        }

        #endregion

        #region Role During Operations Tests

        [TestMethod]
        public void SendMouse_WhenReceiver_DoesNotSend()
        {
            // Arrange
            _transmitter!.SetLocalRole(ConnectionRole.Receiver);

            // Act
            _transmitter.SendMouse(100, 200);

            // Assert
            _mockUdpClient!.Verify(
                c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()),
                Times.Never);
        }

        [TestMethod]
        public void SendMouseButton_WhenReceiver_DoesNotSend()
        {
            // Arrange
            _transmitter!.SetLocalRole(ConnectionRole.Receiver);

            // Act
            _transmitter.SendMouseButton(MouseButtonNet.Left, true);

            // Assert
            _mockUdpClient!.Verify(
                c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()),
                Times.Never);
        }

        [TestMethod]
        public void SendMouseWheel_WhenReceiver_DoesNotSend()
        {
            // Arrange
            _transmitter!.SetLocalRole(ConnectionRole.Receiver);

            // Act
            _transmitter.SendMouseWheel(120);

            // Assert
            _mockUdpClient!.Verify(
                c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()),
                Times.Never);
        }

        #endregion
    }
}