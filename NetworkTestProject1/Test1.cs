// Provides attributes and classes for unit testing, like [TestClass] and [TestMethod].
using Microsoft.VisualStudio.TestTools.UnitTesting;
// A popular mocking library for creating fake objects for testing purposes.
using Moq;
// Contains the network-related classes from the main project, like UdpMouseTransmitter.
using OmniMouse.Network;
// Contains data structures for network packets, like MousePacket.
using OmniMouse.Core.Packets;
// Provides fundamental classes and base types, like Exception and Func.
using System;
// Provides LINQ (Language Integrated Query) capabilities, used here for sequence comparison.
using System.Linq;
// Provides classes for network protocols, like IPAddress and IPEndPoint.
using System.Net;
// Provides classes for implementing network sockets, like Socket.
using System.Net.Sockets;
// A fast binary serialization library used to convert objects to byte arrays.
using MessagePack;

namespace NetworkTestProject1
{
    // The [TestClass] attribute tells the test runner that this class contains unit tests.
    [TestClass]
    public sealed class UdpMouseTransmitterMoreTests
    {
        // This field will hold a "mock" or "fake" version of our IUdpClient.
        // We use a mock to simulate the behavior of the real UdpClient without actual networking.
        // It's nullable (the '?') because it's initialized in TestInitialize, not in a constructor.
        private Mock<IUdpClient>? _mockUdpClient;

        // This field will hold the object we are actually testing.
        // It's nullable for the same reason as above.
        private UdpMouseTransmitter? _transmitter;

        // The [TestInitialize] attribute marks a method that runs *before* each test method in this class.
        // This is useful for setting up a clean state for every test.
        [TestInitialize]
        public void TestInitialize()
        {
            // Creates a new mock object for the IUdpClient interface.
            _mockUdpClient = new Mock<IUdpClient>();

            // We need to mock the 'Client' property of our IUdpClient to avoid an error.
            // The UdpMouseTransmitter accesses client.Client.SetSocketOption(), so the 'Client' property cannot be null.
            // We create a mock of the 'Socket' class itself for this purpose.
            var mockSocket = new Mock<Socket>(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            
            // Here, we configure the mockUdpClient.
            // We tell it: "When your 'Client' property is accessed, return the mockSocket object."
            _mockUdpClient.Setup(c => c.Client).Returns(mockSocket.Object);

            // We create a new instance of the UdpMouseTransmitter class that we want to test.
            // Instead of letting it create a real UdpClient, we "inject" our mock client.
            // This gives us full control over the networking part so we can test our logic in isolation.
            _transmitter = new UdpMouseTransmitter(
                () => _mockUdpClient.Object, // This factory function will be used for creating a client without a port.
                port => _mockUdpClient.Object // This factory function will be used for creating a client with a specific port.
            );
        }

        // The [TestMethod] attribute identifies this method as a single unit test.
        [TestMethod]
        public void StartHost_CreatesUdpClientAndListens()
        {
            // The "Arrange" phase is where we set up the specific conditions for this test.
            #region Arrange
            // This constant holds the port number we expect the StartHost method to use.
            const int expectedPort = 5000;

            // To verify the port, we mock the factory function that UdpMouseTransmitter uses to create a client.
            // This allows us to check exactly how it's called.
            var mockUdpFactoryWithPort = new Mock<Func<int, IUdpClient>>();
            
            // We set up the mock factory. When it's called with any integer ('It.IsAny<int>()'),
            // it should return our pre-configured mock UDP client object.
            // The '!' (null-forgiving operator) tells the compiler we are certain _mockUdpClient is not null here.
            mockUdpFactoryWithPort.Setup(f => f(It.IsAny<int>())).Returns(_mockUdpClient!.Object);

            // We create a new transmitter instance specifically for this test, using our mocked factory.
            var transmitter = new UdpMouseTransmitter(() => _mockUdpClient!.Object, mockUdpFactoryWithPort.Object);
            #endregion

            // The "Act" phase is where we execute the method we want to test.
            #region Act
            // We call the StartHost method on our transmitter instance.
            transmitter.StartHost();
            #endregion

            // The "Assert" phase is where we verify that the outcome of the "Act" phase is correct.
            #region Assert
            // We verify that our mock factory was called exactly one time ('Times.Once').
            // We also check that it was called with the 'expectedPort' (5000).
            mockUdpFactoryWithPort.Verify(f => f(expectedPort), Times.Once);
            #endregion
        }

        [TestMethod]
        public void StartCoHost_WithValidIp_CreatesUdpClient()
        {
            // Arrange: Set up the test with a valid IP address.
            var hostIp = "192.168.1.100"; // A sample valid IP address for the test.

            // Act: Call the method being tested.
            // We use Assert.IsNotNull to be extra safe and ensure our setup method worked.
            Assert.IsNotNull(_transmitter, "_transmitter should not be null before calling StartCoHost.");
            Assert.IsNotNull(_mockUdpClient, "_mockUdpClient should not be null before verifying Send.");
            // We call the StartCoHost method with the valid IP. The '!' is used because we've asserted it's not null.
            _transmitter!.StartCoHost(hostIp);

            // To confirm the client was created and is ready, we try to use it by sending a position.
            _transmitter.SendMousePosition(1, 1);

            // Assert: Verify the outcome.
            // We check that the 'Send' method on our mock client was called exactly once.
            // This proves that StartCoHost successfully configured the transmitter for sending data.
            _mockUdpClient!.Verify(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()), Times.Once);
        }

        // The [ExpectedException] attribute tells the test runner that this test is expected to pass only if it throws a specific type of exception.
        [TestMethod]
        [ExpectedException(typeof(FormatException))]
        public void StartCoHost_WithInvalidIp_ThrowsFormatException()
        {
            // Arrange: Set up the test with an invalid IP address string.
            var invalidHostIp = "this-is-not-an-ip"; // This string cannot be parsed as an IP address.

            // Act: Call the method that should throw the exception.
            Assert.IsNotNull(_transmitter, "_transmitter should not be null before calling StartCoHost.");
            // This line is expected to throw a FormatException because IPAddress.Parse inside StartCoHost will fail.
            _transmitter!.StartCoHost(invalidHostIp);

            // Assert: No explicit assert is needed here. The test will automatically pass if the expected exception is thrown.
            // If no exception or a different type of exception is thrown, the test will fail.
        }

        [TestMethod]
        public void Disconnect_WhenNotConnected_DoesNotThrow()
        {
            // Arrange: No specific setup is needed. We will just use the freshly initialized transmitter.

            // Act: We call the Disconnect method and "record" if any exception occurs.
            Assert.IsNotNull(_transmitter, "_transmitter should not be null before calling Disconnect.");
            // The Record.Exception helper method executes the code and catches any exception that is thrown.
            var exception = Record.Exception(() => _transmitter!.Disconnect());

            // Assert: Check that no exception was recorded.
            // We assert that the 'exception' variable is null, which means the Disconnect method completed without error.
            Assert.IsNull(exception, "Disconnect should not throw an exception if called when not connected.");
        }
    }

    // This is a small helper class to make testing for exceptions cleaner in certain scenarios.
    public static class Record
    {
        // This method takes an 'Action' (a method with no parameters and no return value) as input.
        public static Exception? Exception(Action action)
        {
            try
            {
                // It tries to execute the provided action.
                action();
                // If the action completes without error, it returns null.
                return null;
            }
            catch (Exception ex)
            {
                // If any exception is caught during the action's execution, it returns the exception object.
                return ex;
            }
        }
    }
}
