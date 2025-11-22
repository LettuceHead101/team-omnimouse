using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.Switching;

namespace NetworkTestProject1.SwitchingTests
{
    [TestClass]
    public class Win32ScreenTopologyTests
    {
        [TestMethod]
        public void GetScreenConfiguration_BasicSanity()
        {
            var topo = new Win32ScreenTopology();
            var cfg = topo.GetScreenConfiguration();

            Assert.IsNotNull(cfg);

            var db = cfg.DesktopBounds;
            var pb = cfg.PrimaryScreenBounds;

            Assert.IsTrue(pb.Width > 0 && pb.Height > 0, "Primary screen should have positive dimensions");
            Assert.IsTrue(db.Width >= pb.Width, "Desktop width should be at least primary width");
            Assert.IsTrue(db.Height >= pb.Height, "Desktop height should be at least primary height");

            Assert.IsNotNull(cfg.SensitivePoints);
            Assert.IsTrue(cfg.SensitivePoints.Length % 4 == 0, "Sensitive points should be a multiple of 4 (corners per monitor)");
        }
    }
}
