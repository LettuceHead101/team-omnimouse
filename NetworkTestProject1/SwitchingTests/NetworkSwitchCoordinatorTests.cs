using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.Switching;
using OmniMouse.Network;
using OmniMouse.Hooks;
using System.Drawing;
using System;

namespace NetworkTestProject1.SwitchingTests
{
    [TestClass]
    public class NetworkSwitchCoordinatorTests
    {
        private class FakeSwitcher : IMultiMachineSwitcher
        {
            public event EventHandler<MachineSwitchEventArgs>? SwitchRequested;
            public bool Started { get; private set; }
            public bool Stopped { get; private set; }
            public string[] MachinesStored { get; private set; } = Array.Empty<string>();
            public string ActiveSetByCoordinator { get; private set; } = string.Empty;
            private readonly ScreenBounds _bounds;

            public FakeSwitcher(ScreenBounds bounds)
            {
                _bounds = bounds;
            }

            public void Start() => Started = true;
            public void Stop() => Stopped = true;
            public void UpdateMatrix(string[] machines, bool oneRow = true, bool wrapAround = false) => MachinesStored = machines ?? Array.Empty<string>();
            public void SetActiveMachine(string name) => ActiveSetByCoordinator = name ?? string.Empty;
            public void OnMouseMove(int x, int y) { }
            public ScreenBounds GetScreenBounds() => _bounds;

            public void RaiseSwitch(MachineSwitchEventArgs args) => SwitchRequested?.Invoke(this, args);
        }

        private class FakeTransmitter : IUdpMouseTransmitter
        {
            public (string target, int x, int y)? LastTakeControl;
            // New IUdpMouseTransmitter events required by production code
            public event Action<ConnectionRole>? RoleChanged;
            public event Action<int, int>? TakeControlReceived;
            public event Action<OmniMouse.Network.FileShare.FileOfferPacket>? FileOfferReceived;
            public void StartHost() { }
            public void StartHost(string peerIp) { }
            public void StartCoHost(string hostIp) { }
            public void StartPeer(string peerIp) { }
            public void SetRemotePeer(string hostOrIp) { }
            public void SendMousePosition(int x, int y) { }
            public void SendMouse(int x, int y, bool isDelta = false) { }
            public void SendMouseButton(OmniMouse.Network.MouseButtonNet button, bool isDown) { }
            public void SendMouseWheel(int delta) { }
            public void Disconnect() { }
            public void SendTakeControl(string targetClientId, int x, int y)
            {
                LastTakeControl = (targetClientId, x, y);
            }

            public void SendTakeControl(string targetClientId, int x, int y, OmniMouse.Switching.Direction? entryDirection)
            {
                LastTakeControl = (targetClientId, x, y);
            }

            public void SendLayoutUpdate(int position, string machineId, string displayName) { }
            public void SendGridLayoutUpdate(string machineId, string displayName, int gridX, int gridY) { }
            public void SendFileOffer(OmniMouse.Network.FileShare.FileOfferPacket offer) { }
            public OmniMouse.Network.LayoutCoordinator GetLayoutCoordinator() => null!;
            public string GetLocalMachineId() => "test-client";
        }

        [TestMethod]
        public void Coordinator_SendsTakeControlAndUpdatesSwitcher_OnSwitchRequested()
        {
            var bounds = new ScreenBounds { DesktopBounds = new MyRectangle(0, 0, 800, 600), PrimaryScreenBounds = new MyRectangle(0, 0, 800, 600) };
            var fakeSwitcher = new FakeSwitcher(bounds);
            var fakeTransmitter = new FakeTransmitter();

            var coordinator = new NetworkSwitchCoordinator(fakeSwitcher, fakeTransmitter, "local");

            // event args
            var args = new MachineSwitchEventArgs("Local", "Target", new Point(799, 300), new Point(40000, 30000), SwitchReason.EdgeRight, Direction.Right);

            // do not unsubscribe here; we want the coordinator to handle the event.

            // Raise the event
            fakeSwitcher.RaiseSwitch(args);

            // Allow queued threadpool work item to run (clamping and SetCursorPos may occur)
            System.Threading.Thread.Sleep(50);

            // transmitter was called with corrected coordinates
            Assert.IsNotNull(fakeTransmitter.LastTakeControl);
            Assert.AreEqual("Target", fakeTransmitter.LastTakeControl?.target);

            // For a switch to the right, X is reset to 0 on the new machine, while universal Y is preserved.
            Assert.AreEqual(0, fakeTransmitter.LastTakeControl?.x);
            Assert.AreEqual(args.UniversalCursorPoint.Y, fakeTransmitter.LastTakeControl?.y);

            // Verify coordinator updated switcher active machine
            Assert.AreEqual("Target", fakeSwitcher.ActiveSetByCoordinator);
        }

        [TestMethod]
        public void Coordinator_Cleanup_UnsubscribesFromSwitcher()
        {
            var bounds = new ScreenBounds { DesktopBounds = new MyRectangle(0, 0, 800, 600), PrimaryScreenBounds = new MyRectangle(0, 0, 800, 600) };
            var fakeSwitcher = new FakeSwitcher(bounds);
            var fakeTransmitter = new FakeTransmitter();
            var coordinator = new NetworkSwitchCoordinator(fakeSwitcher, fakeTransmitter, "local");

            coordinator.Cleanup();

            var args = new MachineSwitchEventArgs("Local", "Target2", new Point(0, 0), new Point(100, 100), SwitchReason.EdgeLeft, Direction.Left);
            fakeSwitcher.RaiseSwitch(args);

            // any queued work a moment
            System.Threading.Thread.Sleep(50);

            // Transmitter should not have been called because we unsubscribed
            Assert.IsNull(fakeTransmitter.LastTakeControl);
            Assert.AreEqual(string.Empty, fakeSwitcher.ActiveSetByCoordinator);
        }
    }
}
