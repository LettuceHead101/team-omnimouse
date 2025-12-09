using System;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OmniMouse.Hooks;
using OmniMouse.Network;
using OmniMouse.Switching;

namespace NetworkTestProject1.InputHookTests
{
    // Alias to avoid namespace conflict
    using InputHooksClass = OmniMouse.Hooks.InputHooks;

    /// <summary>
    /// Tests for InputHooks state reset functionality, particularly for disconnect scenarios.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public sealed class InputHooksResetStateTests
    {
        private VirtualScreenMap? _screenMap;
        private UdpMouseTransmitter? _udpTransmitter;
        private InputCoordinator? _coordinator;
        private Mock<IUdpMouseTransmitter>? _mockUdp;
        private Mock<IMultiMachineSwitcher>? _mockSwitcher;
        private InputHooksClass? _sut;

        // Reflection fields for accessing private state
        private readonly FieldInfo _remoteStreamingField = typeof(InputHooksClass).GetField("_remoteStreaming", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly FieldInfo _remoteStreamingDirectionField = typeof(InputHooksClass).GetField("_remoteStreamingDirection", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly FieldInfo _remoteStreamingReleaseAccumField = typeof(InputHooksClass).GetField("_remoteStreamingReleaseAccum", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly FieldInfo _remoteCursorXField = typeof(InputHooksClass).GetField("_remoteCursorX", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly FieldInfo _remoteCursorYField = typeof(InputHooksClass).GetField("_remoteCursorY", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly FieldInfo _instanceField = typeof(InputHooksClass).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

        [TestInitialize]
        public void Setup()
        {
            _screenMap = new VirtualScreenMap();
            _udpTransmitter = new UdpMouseTransmitter(port => new UdpClientAdapter(port));
            _coordinator = new InputCoordinator(_screenMap, _udpTransmitter, "TestHost");
            _mockUdp = new Mock<IUdpMouseTransmitter>();
            _mockSwitcher = new Mock<IMultiMachineSwitcher>();

            _sut = new InputHooksClass(
                _mockUdp.Object,
                _coordinator,
                _mockSwitcher.Object
            );
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Reset all static state
            _remoteStreamingField.SetValue(null, false);
            _remoteStreamingDirectionField.SetValue(null, null);
            _remoteStreamingReleaseAccumField.SetValue(null, 0);
            _remoteCursorXField.SetValue(null, 0);
            _remoteCursorYField.SetValue(null, 0);
            InputHooksClass.SetRemotePeer(null!);
            _instanceField.SetValue(null, null);

            _sut?.UninstallHooks();
            _udpTransmitter?.Disconnect();
        }

        #region ResetPeerConnectionState Tests

        [TestMethod]
        public void ResetPeerConnectionState_ClearsRemoteStreaming()
        {
            // Arrange
            _remoteStreamingField.SetValue(null, true);

            // Act
            InputHooksClass.ResetPeerConnectionState();

            // Assert
            Assert.IsFalse((bool)_remoteStreamingField.GetValue(null)!);
        }

        [TestMethod]
        public void ResetPeerConnectionState_ClearsStreamingDirection()
        {
            // Arrange
            _remoteStreamingDirectionField.SetValue(null, Direction.Right);

            // Act
            InputHooksClass.ResetPeerConnectionState();

            // Assert
            Assert.IsNull(_remoteStreamingDirectionField.GetValue(null));
        }

        [TestMethod]
        public void ResetPeerConnectionState_ClearsReleaseAccumulator()
        {
            // Arrange
            _remoteStreamingReleaseAccumField.SetValue(null, 100);

            // Act
            InputHooksClass.ResetPeerConnectionState();

            // Assert
            Assert.AreEqual(0, (int)_remoteStreamingReleaseAccumField.GetValue(null)!);
        }

        [TestMethod]
        public void ResetPeerConnectionState_ClearsRemoteCursorPosition()
        {
            // Arrange
            _remoteCursorXField.SetValue(null, 500);
            _remoteCursorYField.SetValue(null, 600);

            // Act
            InputHooksClass.ResetPeerConnectionState();

            // Assert
            Assert.AreEqual(0, (int)_remoteCursorXField.GetValue(null)!);
            Assert.AreEqual(0, (int)_remoteCursorYField.GetValue(null)!);
        }

        [TestMethod]
        public void ResetPeerConnectionState_ClearsRemotePeerId()
        {
            // Arrange
            InputHooksClass.SetRemotePeer("TestPeer123");

            // Act
            InputHooksClass.ResetPeerConnectionState();

            // Assert
            Assert.IsNull(InputHooksClass.RemotePeerClientId);
        }

        [TestMethod]
        public void ResetPeerConnectionState_CanBeCalledMultipleTimes()
        {
            // Arrange
            _remoteStreamingField.SetValue(null, true);
            _remoteCursorXField.SetValue(null, 100);

            // Act & Assert - should not throw
            InputHooksClass.ResetPeerConnectionState();
            InputHooksClass.ResetPeerConnectionState();
            InputHooksClass.ResetPeerConnectionState();

            Assert.IsFalse((bool)_remoteStreamingField.GetValue(null)!);
        }

        [TestMethod]
        public void ResetPeerConnectionState_WhenAlreadyReset_DoesNotThrow()
        {
            // Act & Assert
            InputHooksClass.ResetPeerConnectionState();
        }

        #endregion

        #region SetRemotePeer Tests

        [TestMethod]
        public void SetRemotePeer_SetsRemotePeerId()
        {
            // Act
            InputHooksClass.SetRemotePeer("NewPeer");

            // Assert
            Assert.AreEqual("NewPeer", InputHooksClass.RemotePeerClientId);
        }

        [TestMethod]
        public void SetRemotePeer_OverwritesPreviousPeer()
        {
            // Arrange
            InputHooksClass.SetRemotePeer("FirstPeer");

            // Act
            InputHooksClass.SetRemotePeer("SecondPeer");

            // Assert
            Assert.AreEqual("SecondPeer", InputHooksClass.RemotePeerClientId);
        }

        [TestMethod]
        public void SetRemotePeer_WithNull_ClearsPeerId()
        {
            // Arrange
            InputHooksClass.SetRemotePeer("ExistingPeer");

            // Act
            InputHooksClass.SetRemotePeer(null!);

            // Assert
            Assert.IsNull(InputHooksClass.RemotePeerClientId);
        }

        #endregion

        #region EndRemoteStreaming Tests

        [TestMethod]
        public void EndRemoteStreaming_ClearsAllStreamingState()
        {
            // Arrange
            _remoteStreamingField.SetValue(null, true);
            _remoteStreamingDirectionField.SetValue(null, Direction.Left);
            _remoteStreamingReleaseAccumField.SetValue(null, 50);
            _remoteCursorXField.SetValue(null, 200);
            _remoteCursorYField.SetValue(null, 300);

            // Act
            InputHooksClass.EndRemoteStreaming();

            // Assert
            Assert.IsFalse((bool)_remoteStreamingField.GetValue(null)!);
            Assert.IsNull(_remoteStreamingDirectionField.GetValue(null));
            Assert.AreEqual(0, (int)_remoteStreamingReleaseAccumField.GetValue(null)!);
        }

        [TestMethod]
        public void EndRemoteStreaming_WhenNotStreaming_DoesNotThrow()
        {
            // Arrange
            _remoteStreamingField.SetValue(null, false);

            // Act & Assert
            InputHooksClass.EndRemoteStreaming();
        }

        #endregion
    }
}