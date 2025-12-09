using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OmniMouse.Network;

namespace NetworkTestProject1.NetworkTests
{
    /// <summary>
    /// Unit tests for LayoutCoordinator functionality.
    /// </summary>
    [TestClass]
    public class LayoutCoordinatorTests
    {
        private Mock<IUdpMouseTransmitter> _mockTransmitter = null!;
        private LayoutCoordinator _coordinator = null!;
        private const string LocalMachineId = "test-local-machine";

        [TestInitialize]
        public void Setup()
        {
            _mockTransmitter = new Mock<IUdpMouseTransmitter>();
            _coordinator = new LayoutCoordinator(_mockTransmitter.Object, LocalMachineId);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _coordinator = null!;
            _mockTransmitter = null!;
        }

        [TestMethod]
        public void Constructor_AddsLocalMachineToLayout()
        {
            // Assert
            Assert.IsNotNull(_coordinator);
            var layout = _coordinator.CurrentLayout;
            Assert.AreEqual(1, layout.Machines.Count);
            Assert.IsTrue(layout.Machines.Any(m => m.MachineId == LocalMachineId && m.IsLocal));
        }

        [TestMethod]
        public void Constructor_SetsAuthorityToLocalMachine()
        {
            // Assert
            var layout = _coordinator.CurrentLayout;
            Assert.AreEqual(LocalMachineId, layout.AuthorityMachineId);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullTransmitter_ThrowsArgumentNullException()
        {
            // Act
            new LayoutCoordinator(null!, LocalMachineId);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMachineId_ThrowsArgumentNullException()
        {
            // Act
            new LayoutCoordinator(_mockTransmitter.Object, null!);
        }

        [TestMethod]
        public void AnnouncePeerConnected_AddsPeerToLayout()
        {
            // Act
            _coordinator.AnnouncePeerConnected("peer1", "PeerMachine");

            // Assert
            var layout = _coordinator.CurrentLayout;
            Assert.AreEqual(2, layout.Machines.Count);
            Assert.IsTrue(layout.Machines.Any(m => m.MachineId == "peer1" && !m.IsLocal));
        }

        [TestMethod]
        public void AnnouncePeerConnected_SamePeerTwice_DoesNotDuplicate()
        {
            // Act
            _coordinator.AnnouncePeerConnected("peer1", "PeerMachine");
            _coordinator.AnnouncePeerConnected("peer1", "PeerMachine");

            // Assert
            var layout = _coordinator.CurrentLayout;
            Assert.AreEqual(2, layout.Machines.Count);
        }

        [TestMethod]
        public void AnnouncePeerConnected_RaisesLayoutChangedEvent()
        {
            // Arrange
            var eventRaised = false;
            _coordinator.LayoutChanged += (_, _) => eventRaised = true;

            // Act
            _coordinator.AnnouncePeerConnected("peer1", "PeerMachine");

            // Assert
            Assert.IsTrue(eventRaised);
        }

        [TestMethod]
        public void SetLocalPosition_UpdatesLocalMachinePosition()
        {
            // Act
            _coordinator.SetLocalPosition(0);

            // Assert
            var layout = _coordinator.CurrentLayout;
            var local = layout.Machines.First(m => m.IsLocal);
            Assert.AreEqual(0, local.Position);
        }

        [TestMethod]
        public void SetMachinePosition_UpdatesPeerPosition()
        {
            // Arrange
            _coordinator.AnnouncePeerConnected("peer1", "PeerMachine");

            // Act
            _coordinator.SetMachinePosition("peer1", 1);

            // Assert
            var layout = _coordinator.CurrentLayout;
            var peer = layout.Machines.First(m => m.MachineId == "peer1");
            Assert.AreEqual(1, peer.Position);
        }

        [TestMethod]
        public void SetMachinePosition_NonExistentMachine_DoesNotThrow()
        {
            // Act & Assert - should not throw
            _coordinator.SetMachinePosition("nonexistent", 0);
        }

        [TestMethod]
        public void SetMachinePosition_RaisesLayoutChangedEvent()
        {
            // Arrange
            _coordinator.AnnouncePeerConnected("peer1", "PeerMachine");
            var eventRaised = false;
            _coordinator.LayoutChanged += (_, _) => eventRaised = true;

            // Act
            _coordinator.SetMachinePosition("peer1", 1);

            // Assert
            Assert.IsTrue(eventRaised);
        }

        [TestMethod]
        public void CurrentLayout_ReturnsClone()
        {
            // Act
            var layout1 = _coordinator.CurrentLayout;
            var layout2 = _coordinator.CurrentLayout;

            // Assert - should be different instances
            Assert.AreNotSame(layout1, layout2);
        }

        [TestMethod]
        public void CurrentLayout_MachinesInitiallyUnpositioned()
        {
            // Assert
            var layout = _coordinator.CurrentLayout;
            var local = layout.Machines.First(m => m.IsLocal);
            Assert.AreEqual(-1, local.Position);
        }
    }
}
