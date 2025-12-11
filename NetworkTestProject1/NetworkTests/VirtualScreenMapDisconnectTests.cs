using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.Network;

namespace NetworkTestProject1.NetworkTests
{
    /// <summary>
    /// Tests for VirtualScreenMap client and monitor management during disconnect scenarios.
    /// </summary>
    [TestClass]
    public class VirtualScreenMapDisconnectTests
    {
        private VirtualScreenMap _map = null!;

        [TestInitialize]
        public void Setup()
        {
            _map = new VirtualScreenMap();
        }

        [TestCleanup]
        public void Cleanup()
        {
            _map = null!;
        }

        [TestMethod]
        public void RemoveClient_RemovesClientAndMonitors()
        {
            // Arrange
            _map.AddOrUpdateClient(new ClientPc { ClientId = "client1", FriendlyName = "Client1" });
            _map.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = "client1",
                FriendlyName = "Monitor1",
                GlobalBounds = new RectInt(0, 0, 1920, 1080),
                LocalBounds = new RectInt(0, 0, 1920, 1080)
            });

            // Act
            var result = _map.RemoveClient("client1");

            // Assert
            Assert.IsTrue(result);
            var clients = _map.GetClientsSnapshot();
            var monitors = _map.GetMonitorsSnapshot();
            Assert.AreEqual(0, clients.Count);
            Assert.AreEqual(0, monitors.Count);
        }

        [TestMethod]
        public void RemoveClient_NonExistent_ReturnsFalse()
        {
            // Act
            var result = _map.RemoveClient("nonexistent");

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void RemoveClient_PreservesOtherClients()
        {
            // Arrange
            _map.AddOrUpdateClient(new ClientPc { ClientId = "client1", FriendlyName = "Client1" });
            _map.AddOrUpdateClient(new ClientPc { ClientId = "client2", FriendlyName = "Client2" });

            // Act
            _map.RemoveClient("client1");

            // Assert
            var clients = _map.GetClientsSnapshot();
            Assert.AreEqual(1, clients.Count);
            Assert.AreEqual("client2", clients[0].ClientId);
        }

        [TestMethod]
        public void RemoveClient_OnlyRemovesOwnedMonitors()
        {
            // Arrange
            _map.AddOrUpdateClient(new ClientPc { ClientId = "client1", FriendlyName = "Client1" });
            _map.AddOrUpdateClient(new ClientPc { ClientId = "client2", FriendlyName = "Client2" });
            _map.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = "client1",
                FriendlyName = "Monitor1",
                GlobalBounds = new RectInt(0, 0, 1920, 1080),
                LocalBounds = new RectInt(0, 0, 1920, 1080)
            });
            _map.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = "client2",
                FriendlyName = "Monitor2",
                GlobalBounds = new RectInt(1920, 0, 1920, 1080),
                LocalBounds = new RectInt(0, 0, 1920, 1080)
            });

            // Act
            _map.RemoveClient("client1");

            // Assert
            var monitors = _map.GetMonitorsSnapshot();
            Assert.AreEqual(1, monitors.Count);
            Assert.AreEqual("client2", monitors[0].OwnerClientId);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void RemoveClient_NullClientId_ThrowsException()
        {
            // Act
            _map.RemoveClient(null!);
        }

        [TestMethod]
        public void RemoveMonitor_RemovesMonitor()
        {
            // Arrange
            var monitorId = Guid.NewGuid().ToString();
            _map.AddOrUpdateClient(new ClientPc { ClientId = "client1", FriendlyName = "Client1" });
            _map.AddOrUpdateMonitor(new MonitorInfo
            {
                MonitorId = monitorId,
                OwnerClientId = "client1",
                FriendlyName = "Monitor1",
                GlobalBounds = new RectInt(0, 0, 1920, 1080),
                LocalBounds = new RectInt(0, 0, 1920, 1080)
            });

            // Act
            var result = _map.RemoveMonitor(monitorId);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(0, _map.GetMonitorsSnapshot().Count);
        }

        [TestMethod]
        public void RemoveMonitor_NonExistent_ReturnsFalse()
        {
            // Act
            var result = _map.RemoveMonitor("nonexistent");

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ConcurrentAddAndRemove_ThreadSafe()
        {
            // Arrange
            var tasks = new Task[10];

            // Act
            for (int i = 0; i < 5; i++)
            {
                var index = i;
                tasks[i] = Task.Run(() =>
                {
                    _map.AddOrUpdateClient(new ClientPc { ClientId = $"client{index}", FriendlyName = $"Client{index}" });
                });
            }

            for (int i = 5; i < 10; i++)
            {
                var index = i - 5;
                tasks[i] = Task.Run(() =>
                {
                    _map.RemoveClient($"client{index}");
                });
            }

            // Assert - should not throw
            Task.WaitAll(tasks);
        }

        [TestMethod]
        public void GetMonitorsSnapshot_ReturnsAllMonitors()
        {
            // Arrange
            _map.AddOrUpdateClient(new ClientPc { ClientId = "client1", FriendlyName = "Client1" });
            _map.AddOrUpdateClient(new ClientPc { ClientId = "client2", FriendlyName = "Client2" });

            _map.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = "client1",
                FriendlyName = "Monitor1",
                GlobalBounds = new RectInt(0, 0, 1920, 1080),
                LocalBounds = new RectInt(0, 0, 1920, 1080)
            });

            _map.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = "client2",
                FriendlyName = "Monitor2",
                GlobalBounds = new RectInt(1920, 0, 1920, 1080),
                LocalBounds = new RectInt(0, 0, 1920, 1080)
            });

            // Act
            var monitors = _map.GetMonitorsSnapshot();

            // Assert
            Assert.AreEqual(2, monitors.Count);
        }

        [TestMethod]
        public void GetClientsSnapshot_ReturnsAllClients()
        {
            // Arrange
            _map.AddOrUpdateClient(new ClientPc { ClientId = "client1", FriendlyName = "Client1" });
            _map.AddOrUpdateClient(new ClientPc { ClientId = "client2", FriendlyName = "Client2" });

            // Act
            var clients = _map.GetClientsSnapshot();

            // Assert
            Assert.AreEqual(2, clients.Count);
        }

        [TestMethod]
        public void FindMonitorAt_AfterRemoveClient_ReturnsNull()
        {
            // Arrange
            _map.AddOrUpdateClient(new ClientPc { ClientId = "client1", FriendlyName = "Client1" });
            _map.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = "client1",
                FriendlyName = "Monitor1",
                GlobalBounds = new RectInt(0, 0, 1920, 1080),
                LocalBounds = new RectInt(0, 0, 1920, 1080)
            });

            // Act
            _map.RemoveClient("client1");
            var monitor = _map.FindMonitorAt(100, 100);

            // Assert
            Assert.IsNull(monitor);
        }

        [TestMethod]
        public void LayoutChanged_FiresOnRemoveClient()
        {
            // Arrange
            _map.AddOrUpdateClient(new ClientPc { ClientId = "client1", FriendlyName = "Client1" });
            var eventFired = false;
            _map.LayoutChanged += () => eventFired = true;

            // Act
            _map.RemoveClient("client1");

            // Wait for async event
            System.Threading.Thread.Sleep(100);

            // Assert
            Assert.IsTrue(eventFired);
        }
    }
}
