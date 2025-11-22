using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.Switching;
using System.Drawing;
using System;

namespace NetworkTestProject1.SwitchingTests
{
    [TestClass]
    public class ISwitchPolicyTests
    {
        private class FakeSwitchPolicy : ISwitchPolicy
        {
            public int EdgeThresholdPixels { get; set; } = 5;
            public int CooldownMilliseconds { get; set; } = 0;
            public bool BlockAtCorners { get; set; } = false;
            public bool UseRelativeMovement { get; set; } = false;

            private DateTime? _lastSwitchTime;

            public SwitchDecision Evaluate(MouseMoveContext context)
            {
                if (context == null) throw new ArgumentNullException(nameof(context));

                // Cooldown
                if (_lastSwitchTime.HasValue && CooldownMilliseconds > 0)
                {
                    var delta = (context.Timestamp - _lastSwitchTime.Value).TotalMilliseconds;
                    if (delta < CooldownMilliseconds)
                        return SwitchDecision.NoSwitch(SwitchReason.CooldownActive);
                }

                var leftEdge = context.DesktopBounds.Left + EdgeThresholdPixels;
                var rightEdge = context.DesktopBounds.Right - EdgeThresholdPixels;
                var topEdge = context.DesktopBounds.Top + EdgeThresholdPixels;
                var bottomEdge = context.DesktopBounds.Bottom - EdgeThresholdPixels;

                // Corner blocking
                if (BlockAtCorners)
                {
                    var atLeft = context.RawPixel.X <= leftEdge;
                    var atRight = context.RawPixel.X >= rightEdge;
                    var atTop = context.RawPixel.Y <= topEdge;
                    var atBottom = context.RawPixel.Y >= bottomEdge;
                    if ((atLeft || atRight) && (atTop || atBottom))
                        return SwitchDecision.NoSwitch(SwitchReason.CornerBlocked);
                }

                if (context.RawPixel.X <= leftEdge)
                {
                    _lastSwitchTime = context.Timestamp;
                    return SwitchDecision.Switch("LeftMachine", SwitchReason.EdgeLeft, new Point(0, 0), Direction.Left);
                }

                if (context.RawPixel.X >= rightEdge)
                {
                    _lastSwitchTime = context.Timestamp;
                    return SwitchDecision.Switch("RightMachine", SwitchReason.EdgeRight, new Point(0, 0), Direction.Right);
                }

                return SwitchDecision.NoSwitch(SwitchReason.None);
            }
        }

        private MouseMoveContext BuildContext(Point raw, MyRectangle desktop)
        {
            return new MouseMoveContext
            {
                Timestamp = DateTime.UtcNow,
                RawPixel = raw,
                DesktopBounds = desktop,
                PrimaryBounds = desktop,
                CurrentMachine = "Test",
                IsController = true,
                SensitivePoints = Array.Empty<Point>()
            };
        }

        [TestMethod]
        public void Properties_AreGetSet()
        {
            var p = new FakeSwitchPolicy();
            p.EdgeThresholdPixels = 10;
            p.CooldownMilliseconds = 2000;
            p.BlockAtCorners = true;
            p.UseRelativeMovement = true;

            Assert.AreEqual(10, p.EdgeThresholdPixels);
            Assert.AreEqual(2000, p.CooldownMilliseconds);
            Assert.IsTrue(p.BlockAtCorners);
            Assert.IsTrue(p.UseRelativeMovement);
        }

        [TestMethod]
        public void Evaluate_ReturnsNoSwitch_WhenNotNearEdges()
        {
            var policy = new FakeSwitchPolicy { EdgeThresholdPixels = 5 };
            var desktop = new MyRectangle(0, 0, 100, 100);
            var ctx = BuildContext(new Point(50, 50), desktop);

            var dec = policy.Evaluate(ctx);
            Assert.IsFalse(dec.ShouldSwitch);
            Assert.AreEqual(SwitchReason.None, dec.Reason);
        }

        [TestMethod]
        public void Evaluate_SwitchesLeft_WhenNearLeftEdge()
        {
            var policy = new FakeSwitchPolicy { EdgeThresholdPixels = 10 };
            var desktop = new MyRectangle(0, 0, 200, 200);
            var ctx = BuildContext(new Point(5, 50), desktop);

            var dec = policy.Evaluate(ctx);
            Assert.IsTrue(dec.ShouldSwitch);
            Assert.AreEqual(SwitchReason.EdgeLeft, dec.Reason);
            Assert.AreEqual("LeftMachine", dec.TargetMachine);
            Assert.AreEqual(Direction.Left, dec.Direction);
        }

        [TestMethod]
        public void Evaluate_SwitchesRight_WhenNearRightEdge()
        {
            var policy = new FakeSwitchPolicy { EdgeThresholdPixels = 10 };
            var desktop = new MyRectangle(0, 0, 200, 200);
            var ctx = BuildContext(new Point(195, 50), desktop);

            var dec = policy.Evaluate(ctx);
            Assert.IsTrue(dec.ShouldSwitch);
            Assert.AreEqual(SwitchReason.EdgeRight, dec.Reason);
            Assert.AreEqual("RightMachine", dec.TargetMachine);
            Assert.AreEqual(Direction.Right, dec.Direction);
        }

        [TestMethod]
        public void Evaluate_RespectsCooldown_PreventsRapidSwitching()
        {
            var policy = new FakeSwitchPolicy { EdgeThresholdPixels = 10, CooldownMilliseconds = 1000 };
            var desktop = new MyRectangle(0, 0, 200, 200);
            var now = DateTime.UtcNow;

            var ctx1 = new MouseMoveContext { Timestamp = now, RawPixel = new Point(5, 50), DesktopBounds = desktop, PrimaryBounds = desktop };
            var dec1 = policy.Evaluate(ctx1);
            Assert.IsTrue(dec1.ShouldSwitch);

            var ctx2 = new MouseMoveContext { Timestamp = now.AddMilliseconds(100), RawPixel = new Point(5, 50), DesktopBounds = desktop, PrimaryBounds = desktop };
            var dec2 = policy.Evaluate(ctx2);
            Assert.IsFalse(dec2.ShouldSwitch);
            Assert.AreEqual(SwitchReason.CooldownActive, dec2.Reason);
        }

        [TestMethod]
        public void Evaluate_BlockAtCorners_PreventsSwitchAtCorners()
        {
            var policy = new FakeSwitchPolicy { EdgeThresholdPixels = 10, BlockAtCorners = true };
            var desktop = new MyRectangle(0, 0, 200, 200);
            // top-left corner
            var ctx = BuildContext(new Point(0, 0), desktop);

            var dec = policy.Evaluate(ctx);
            Assert.IsFalse(dec.ShouldSwitch);
            Assert.AreEqual(SwitchReason.CornerBlocked, dec.Reason);
        }
    }
}
