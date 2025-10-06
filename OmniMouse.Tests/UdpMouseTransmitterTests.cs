using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OmniMouse.Network;
using OmniMouse.Core.Packets;
using System.Net;
using MessagePack;
using System.Linq;

namespace OmniMouse.Tests
{
    [TestClass]
    public class UdpMouseTransmitterTests
    {
        [TestMethod]
        public void SendMousePosition_WhenCoHost_SendsCorrectData()
        {
            // Arrange
            var mockUdpClient = new Mock<IUdpClient>();
            var transmitter = new UdpMouseTransmitter(() => mockUdpClient.Object, port => mockUdpClient.Object);
            
            transmitter.StartCoHost("127.0.0.1");

            int x = 100;
            int y = 200;
            var expectedPacket = new MousePacket { X = x, Y = y };
            var expectedBytes = MessagePackSerializer.Serialize(expectedPacket);

            // Act
            transmitter.SendMousePosition(x, y);

            // Assert
            mockUdpClient.Verify(c => c.Send(
                It.Is<byte[]>(bytes => bytes.SequenceEqual(expectedBytes)),
                expectedBytes.Length,
                It.IsAny<IPEndPoint>()),
                Times.Once);
        }

        [TestMethod]
        public void SendMousePosition_WhenNotCoHost_DoesNotSendData()
        {
            // Arrange
            var mockUdpClient = new Mock<IUdpClient>();
            var transmitter = new UdpMouseTransmitter(() => mockUdpClient.Object, port => mockUdpClient.Object);

            // Not calling StartCoHost, so it's in host mode.

            // Act
            transmitter.SendMousePosition(100, 200);

            // Assert
            mockUdpClient.Verify(c => c.Send(
                It.IsAny<byte[]>(),
                It.IsAny<int>(),
                It.IsAny<IPEndPoint>()),
                Times.Never);
        }

        [TestMethod]
        public void Disconnect_ClosesUdpClient()
        {
            // Arrange
            var mockUdpClient = new Mock<IUdpClient>();
            var transmitter = new UdpMouseTransmitter(() => mockUdpClient.Object, port => mockUdpClient.Object);
            transmitter.StartCoHost("127.0.0.1");

            // Act
            transmitter.Disconnect();

            // Assert
            mockUdpClient.Verify(c => c.Close(), Times.Once);
        }
    }
}