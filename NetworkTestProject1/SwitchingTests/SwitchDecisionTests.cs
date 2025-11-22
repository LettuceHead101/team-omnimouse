using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Drawing;
using OmniMouse.Switching;

namespace NetworkTestProject1.SwitchingTests
{
    [TestClass]
    public class SwitchDecisionTests
    {
        [TestMethod]
        public void NoSwitch_CreatesCorrectDecision()
        {
            var decision = SwitchDecision.NoSwitch(SwitchReason.CooldownActive);

            Assert.IsFalse(decision.ShouldSwitch);
            Assert.AreEqual(SwitchReason.CooldownActive, decision.Reason);
            Assert.IsNull(decision.TargetMachine);
        }

        [TestMethod]
        public void Switch_CreatesCorrectDecision()
        {
            var p = new Point(123, 456);
            var decision = SwitchDecision.Switch("target1", SwitchReason.EdgeRight, p, Direction.Right);

            Assert.IsTrue(decision.ShouldSwitch);
            Assert.AreEqual("target1", decision.TargetMachine);
            Assert.AreEqual(SwitchReason.EdgeRight, decision.Reason);
            Assert.AreEqual(p, decision.UniversalPoint);
            Assert.AreEqual(Direction.Right, decision.Direction);
        }
    }
}
