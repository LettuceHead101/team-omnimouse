using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.Switching;
using System;
using System.Drawing;

namespace NetworkTestProject1.SwitchingTests
{
    [TestClass]
    public class IMultiMachineSwitcherTests
    {
        private class FakeMultiMachineSwitcher : IMultiMachineSwitcher
        {
            public event EventHandler<MachineSwitchEventArgs>? SwitchRequested;

            public bool IsRunning { get; private set; }

            public string[] MachinesStored { get; private set; } = Array.Empty<string>();
            public bool OneRow { get; private set; }
            public bool Wrap { get; private set; }
            public string Active { get; private set; } = string.Empty;
            private ScreenBounds _screenBounds = new ScreenBounds();

            public void Start() => IsRunning = true;
            public void Stop() => IsRunning = false;

            public void UpdateMatrix(string[] machines, bool oneRow = true, bool wrapAround = false)
            {
                MachinesStored = machines ?? Array.Empty<string>();
                OneRow = oneRow;
                Wrap = wrapAround;
            }

            public void SetActiveMachine(string name) => Active = name ?? string.Empty;

            public void OnMouseMove(int x, int y)
            {
                // Simple rule for tests: if x < 0, attempt left neighbor; if x > 10000, attempt right neighbor
                var layout = new DefaultMachineLayout(MachinesStored, oneRow: OneRow, wrapAround: Wrap);

                Direction dir = Direction.Left;
                string? target = null;
                if (x < 0)
                {
                    target = layout.GetNeighbor(Direction.Left, Active);
                    dir = Direction.Left;
                }
                else if (x > 10000)
                {
                    target = layout.GetNeighbor(Direction.Right, Active);
                    dir = Direction.Right;
                }

                if (!string.IsNullOrWhiteSpace(target))
                {
                    var args = new MachineSwitchEventArgs(Active, target, new Point(x, y), new Point(x, y), SwitchReason.EdgeLeft, dir);
                    SwitchRequested?.Invoke(this, args);
                }
            }

            public ScreenBounds GetScreenBounds() => _screenBounds;
        }

        [TestMethod]
        public void StartStop_TogglesRunningState()
        {
            var fm = new FakeMultiMachineSwitcher();
            Assert.IsFalse(fm.IsRunning);
            fm.Start();
            Assert.IsTrue(fm.IsRunning);
            fm.Stop();
            Assert.IsFalse(fm.IsRunning);
        }

        [TestMethod]
        public void UpdateMatrix_StoresMachinesAndFlags()
        {
            var fm = new FakeMultiMachineSwitcher();
            fm.UpdateMatrix(new[] { "A", "B", "C" }, oneRow: false, wrapAround: true);
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, fm.MachinesStored);
            Assert.IsFalse(fm.OneRow);
            Assert.IsTrue(fm.Wrap);
        }

        [TestMethod]
        public void SetActiveMachine_ChangesActive()
        {
            var fm = new FakeMultiMachineSwitcher();
            fm.SetActiveMachine("Node1");
            Assert.AreEqual("Node1", fm.Active);
        }

        [TestMethod]
        public void OnMouseMove_RaisesSwitchRequested_WhenNeighborExists()
        {
            var fm = new FakeMultiMachineSwitcher();
            fm.UpdateMatrix(new[] { "A", "B", "C" }, oneRow: true, wrapAround: false);
            fm.SetActiveMachine("B");

            MachineSwitchEventArgs? received = null;
            fm.SwitchRequested += (s, e) => received = e;

            // simulate moving left off the screen
            fm.OnMouseMove(-1, 50);

            Assert.IsNotNull(received);
            Assert.AreEqual("B", received!.FromMachine);
            Assert.AreEqual("A", received.ToMachine);
            Assert.AreEqual(new Point(-1, 50), received.RawCursorPoint);
            Assert.AreEqual(Direction.Left, received.Direction);
        }

        [TestMethod]
        public void OnMouseMove_DoesNotRaise_WhenNoNeighbor()
        {
            var fm = new FakeMultiMachineSwitcher();
            fm.UpdateMatrix(new[] { "Solo", string.Empty, string.Empty, string.Empty }, oneRow: true, wrapAround: false);
            fm.SetActiveMachine("Solo");

            bool called = false;
            fm.SwitchRequested += (s, e) => called = true;

            fm.OnMouseMove(-1, 0);

            Assert.IsFalse(called);
        }
    }
}
