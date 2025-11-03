// Provides attributes and classes for unit testing, like [TestClass] and [TestMethod].
using Microsoft.VisualStudio.TestTools.UnitTesting;
// A popular mocking library for creating fake objects for testing purposes.
using Moq;
// Contains the network-related classes from the main project, like UdpMouseTransmitter.
using OmniMouse.Network;
// Provides fundamental classes and base types, like Exception and Func.
using System;
// Provides LINQ (Language Integrated Query) capabilities, used here for sequence comparison.
using System.Linq;
// Provides classes for network protocols, like IPAddress and IPEndPoint.
using System.Net;
// Provides classes for implementing network sockets, like Socket.
using System.Net.Sockets;

namespace NetworkTestProject1
{
    // The [TestClass] attribute tells the test runner that this class contains unit tests.
    [TestClass]
    public sealed class UdpMouseTransmitterMoreTests
    {
        private Mock<IUdpClient>? _mockUdpClient;
        private UdpMouseTransmitter? _transmitter;

        [TestInitialize]
        public void TestInitialize()
        {
            _mockUdpClient = new Mock<IUdpClient>();
            var mockSocket = new Mock<Socket>(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _mockUdpClient.Setup(c => c.Client).Returns(mockSocket.Object);

            _transmitter = new UdpMouseTransmitter(
                () => _mockUdpClient.Object,
                port => _mockUdpClient.Object
            );
        }

        [TestMethod]
        public void StartHost_CreatesUdpClientAndListens()
        {
            const int expectedPort = 5000;
            var mockUdpFactoryWithPort = new Mock<Func<int, IUdpClient>>();
            mockUdpFactoryWithPort.Setup(f => f(It.IsAny<int>())).Returns(_mockUdpClient!.Object);
            var transmitter = new UdpMouseTransmitter(() => _mockUdpClient!.Object, mockUdpFactoryWithPort.Object);

            transmitter.StartHost();

            mockUdpFactoryWithPort.Verify(f => f(expectedPort), Times.Once);
        }

        [TestMethod]
        public void StartCoHost_WithValidIp_CreatesUdpClient()
        {
            var hostIp = "192.168.1.100";
            Assert.IsNotNull(_transmitter);
            Assert.IsNotNull(_mockUdpClient);

            _transmitter!.StartCoHost(hostIp);

            // Send any normalized to confirm path is wired
            _transmitter.SendNormalizedMousePosition(0.5f, 0.5f);

            _mockUdpClient!.Verify(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()), Times.Once);
        }

        [TestMethod]
        [ExpectedException(typeof(FormatException))]
        public void StartCoHost_WithInvalidIp_ThrowsFormatException()
        {
            var invalidHostIp = "this-is-not-an-ip";
            Assert.IsNotNull(_transmitter);
            _transmitter!.StartCoHost(invalidHostIp);
        }

        [TestMethod]
        public void Disconnect_WhenNotConnected_DoesNotThrow()
        {
            Assert.IsNotNull(_transmitter);
            var exception = Record.Exception(() => _transmitter!.Disconnect());
            Assert.IsNull(exception, "Disconnect should not throw an exception if called when not connected.");
        }
    }

    public static class Record
    {
        public static Exception? Exception(Action action)
        {
            try { action(); return null; }
            catch (Exception ex) { return ex; }
        }
    }
}
