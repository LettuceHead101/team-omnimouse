using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.Switching;
using System;
using System.Drawing;

namespace NetworkTestProject1.SwitchingTests
{
    [TestClass]
    public class MultiMachineSwitcherTests
    {
        private class FakeTopology : IScreenTopology
        {
            private ScreenBounds _cfg;
            public FakeTopology(ScreenBounds cfg) => _cfg = cfg;
            public ScreenBounds GetScreenConfiguration() => _cfg;
            public bool GetCursorPosition(out Point position) { position = new Point(0,0); return false; }
            public void SetConfig(ScreenBounds cfg) => _cfg = cfg;
        }

        private class SimplePolicy : ISwitchPolicy
        {
            public int EdgeThresholdPixels { get; set; }
            public int CooldownMilliseconds { get; set; }
            public bool BlockAtCorners { get; set; }
            public bool UseRelativeMovement { get; set; }

            public MouseMoveContext? LastContext;
            private readonly SwitchDecision _decisionToReturn;

            public SimplePolicy(SwitchDecision decisionToReturn)
            {
                _decisionToReturn = decisionToReturn;
            }

            public SwitchDecision Evaluate(MouseMoveContext context)
            {
                LastContext = context;
                return _decisionToReturn;
            }
        }

        [TestMethod]
        public void Start_PopulatesCurrentBounds()
        {
            var sb = new ScreenBounds { DesktopBounds = new MyRectangle(0,0,800,600), PrimaryScreenBounds = new MyRectangle(0,0,800,600) };
            var topo = new FakeTopology(sb);
            var layout = new DefaultMachineLayout();
            var policy = new SimplePolicy(SwitchDecision.NoSwitch(SwitchReason.None));
            var mapper = new DefaultCoordinateMapper();

            var sut = new MultiMachineSwitcher(topo, layout, policy, mapper);
            sut.Start();

            var got = sut.GetScreenBounds();
            Assert.AreEqual(sb.DesktopBounds.Left, got.DesktopBounds.Left);
            Assert.AreEqual(sb.PrimaryScreenBounds.Right, got.PrimaryScreenBounds.Right);
        }

        [TestMethod]
        public void UpdateMatrix_ThrowsOnEmptyList()
        {
            var topo = new FakeTopology(new ScreenBounds());
            var layout = new DefaultMachineLayout();
            var policy = new SimplePolicy(SwitchDecision.NoSwitch(SwitchReason.None));
            var mapper = new DefaultCoordinateMapper();
            var sut = new MultiMachineSwitcher(topo, layout, policy, mapper);

            Assert.ThrowsException<ArgumentException>(() => sut.UpdateMatrix(Array.Empty<string>()));
        }

        [TestMethod]
        public void SetActiveMachine_ThrowsOnEmptyName()
        {
            var topo = new FakeTopology(new ScreenBounds());
            var layout = new DefaultMachineLayout();
            var policy = new SimplePolicy(SwitchDecision.NoSwitch(SwitchReason.None));
            var mapper = new DefaultCoordinateMapper();
            var sut = new MultiMachineSwitcher(topo, layout, policy, mapper);

            Assert.ThrowsException<ArgumentException>(() => sut.SetActiveMachine("  "));
        }

        [TestMethod]
        public void SetActiveMachine_UpdatesLayoutCurrent()
        {
            var topo = new FakeTopology(new ScreenBounds());
            var layout = new DefaultMachineLayout();
            var policy = new SimplePolicy(SwitchDecision.NoSwitch(SwitchReason.None));
            var mapper = new DefaultCoordinateMapper();
            var sut = new MultiMachineSwitcher(topo, layout, policy, mapper);

            sut.SetActiveMachine("NodeA");
            sut.UpdateMatrix(new[] { "NodeA", "NodeB" });

            Assert.AreEqual("NodeA", layout.CurrentMachine);
        }

        [TestMethod]
        public void OnMouseMove_RaisesSwitchRequested_WhenPolicyReturnsSwitch()
        {
            var sb = new ScreenBounds { DesktopBounds = new MyRectangle(0,0,800,600), PrimaryScreenBounds = new MyRectangle(0,0,800,600) };
            var topo = new FakeTopology(sb);
            var layout = new DefaultMachineLayout(new[] { "A","B" });
            var decision = SwitchDecision.Switch("B", SwitchReason.EdgeRight, new Point(123, 456), Direction.Right);
            var policy = new SimplePolicy(decision);
            var mapper = new DefaultCoordinateMapper();
            var sut = new MultiMachineSwitcher(topo, layout, policy, mapper);

            sut.SetActiveMachine("A");
            bool raised = false;
            MachineSwitchEventArgs? args = null;
            sut.SwitchRequested += (s, e) => { raised = true; args = e; };

            sut.Start();
            sut.OnMouseMove(790, 300);

            Assert.IsTrue(raised);
            Assert.IsNotNull(args);
            Assert.AreEqual("A", args!.FromMachine);
            Assert.AreEqual("B", args.ToMachine);
            Assert.AreEqual(Direction.Right, args.Direction);
        }

        [TestMethod]
        public void OnMouseMove_DoesNotRaise_WhenNotRunning()
        {
            var topo = new FakeTopology(new ScreenBounds());
            var layout = new DefaultMachineLayout(new[] { "A","B" });
            var decision = SwitchDecision.Switch("B", SwitchReason.EdgeRight, new Point(0,0), Direction.Right);
            var policy = new SimplePolicy(decision);
            var mapper = new DefaultCoordinateMapper();
            var sut = new MultiMachineSwitcher(topo, layout, policy, mapper);

            bool called = false;
            sut.SwitchRequested += (s, e) => called = true;

            // Do not call Start()
            sut.OnMouseMove(1000, 1000);
            Assert.IsFalse(called);
        }

        [TestMethod]
        public void RefreshScreenConfiguration_UpdatesBounds()
        {
            var sb1 = new ScreenBounds { DesktopBounds = new MyRectangle(0,0,800,600) };
            var sb2 = new ScreenBounds { DesktopBounds = new MyRectangle(0,0,1024,768) };
            var topo = new FakeTopology(sb1);
            var layout = new DefaultMachineLayout();
            var policy = new SimplePolicy(SwitchDecision.NoSwitch(SwitchReason.None));
            var mapper = new DefaultCoordinateMapper();
            var sut = new MultiMachineSwitcher(topo, layout, policy, mapper);

            sut.Start();
            var before = sut.GetScreenBounds();
            Assert.AreEqual(800, before.DesktopBounds.Width);

            // change topology config
            topo.SetConfig(sb2);
            sut.RefreshScreenConfiguration();
            var after = sut.GetScreenBounds();
            Assert.AreEqual(1024, after.DesktopBounds.Width);
        }
    }
}
