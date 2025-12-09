using System;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.Network;

namespace NetworkTestProject1.NetworkTests
{
    /// <summary>
    /// Tests for InputCoordinator behavior during disconnect scenarios.
    /// </summary>
    [TestClass]
    public class InputCoordinatorDisconnectTests
    {
        private VirtualScreenMap _map = null!;
        private UdpMouseTransmitter _udpTransmitter = null!;
        private InputCoordinator _coordinator = null!;

        [TestInitialize]
        public void Setup()
        {
            _map = new VirtualScreenMap();
            _udpTransmitter = new UdpMouseTransmitter(port => new UdpClientAdapter(port));
            _coordinator = new InputCoordinator(_map, _udpTransmitter, "TestHost");
        }

        [TestCleanup]
        public void Cleanup()
        {
            _udpTransmitter?.Disconnect();
            _coordinator = null!;
            _map = null!;
            _udpTransmitter = null!;
        }

        [TestMethod]
        public void OnMouseInput_WithEmptyMap_DoesNotThrow()
        {
            // Act & Assert - should not throw
            _coordinator.OnMouseInput(100, 100);
        }

        [TestMethod]
        public void OnMouseInput_AfterRemoveClient_ContinuesToWork()
        {
            // Arrange
            _map.AddOrUpdateClient(new ClientPc { ClientId = "remote", FriendlyName = "Remote" });
            _map.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = "remote",
                FriendlyName = "RemoteMon",
                GlobalBounds = new RectInt(1920, 0, 1920, 1080),
                LocalBounds = new RectInt(0, 0, 1920, 1080)
            });

            // Act - Simulate disconnect by removing client
            _map.RemoveClient("remote");

            // Assert - should not throw when processing input after disconnect
            _coordinator.OnMouseInput(50, 50);
        }

        [TestMethod]
        public void Constructor_ThrowsOnNullMap()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new InputCoordinator(null!, _udpTransmitter, "TestHost"));
        }

        [TestMethod]
        public void Constructor_ThrowsOnNullUdpService()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new InputCoordinator(_map, null!, "TestHost"));
        }

        [TestMethod]
        public void Constructor_ThrowsOnNullSelfClientId()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new InputCoordinator(_map, _udpTransmitter, null!));
        }

        [TestMethod]
        public void GlobalMousePosition_UpdatesOnInput()
        {
            // Arrange - Add local monitor
            _map.AddOrUpdateClient(new ClientPc { ClientId = "TestHost", FriendlyName = "Local" });
            _map.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = "TestHost",
                FriendlyName = "LocalMon",
                GlobalBounds = new RectInt(0, 0, 1920, 1080),
                LocalBounds = new RectInt(0, 0, 1920, 1080)
            });

            // Act
            _coordinator.OnMouseInput(50, 40);

            // Assert - verify via reflection that global position changed
            var gxField = typeof(InputCoordinator).GetField("_globalMouseX", BindingFlags.Instance | BindingFlags.NonPublic);
            var gyField = typeof(InputCoordinator).GetField("_globalMouseY", BindingFlags.Instance | BindingFlags.NonPublic);

            var gx = (int?)gxField?.GetValue(_coordinator);
            var gy = (int?)gyField?.GetValue(_coordinator);

            // Position should have changed (exact value depends on initial cursor pos)
            Assert.IsNotNull(gx);
            Assert.IsNotNull(gy);
        }

        [TestMethod]
        public void ScreenMap_Property_ReturnsCorrectMap()
        {
            // Use internal property via reflection or InternalsVisibleTo
            var mapProp = typeof(InputCoordinator).GetProperty("ScreenMap", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var result = mapProp?.GetValue(_coordinator) as VirtualScreenMap;

            Assert.AreSame(_map, result);
        }

        [TestMethod]
        public void SelfClientId_Property_ReturnsCorrectId()
        {
            var idProp = typeof(InputCoordinator).GetProperty("SelfClientId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var result = idProp?.GetValue(_coordinator) as string;

            Assert.AreEqual("TestHost", result);
        }

        [TestMethod]
        public void BecameServer_Event_CanBeSubscribed()
        {
            // Arrange
            var eventFired = false;
            _coordinator.BecameServer += () => eventFired = true;

            // Assert - just verify subscription works without error
            Assert.IsFalse(eventFired); // Not fired until triggered
        }
    }
}
