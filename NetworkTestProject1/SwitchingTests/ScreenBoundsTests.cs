using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Drawing;
using OmniMouse.Switching;

namespace NetworkTestProject1.SwitchingTests
{
    [TestClass]
    public class ScreenBoundsTests
    {
        [TestMethod]
        public void MyRectangle_Contains_And_Dimensions()
        {
            var r = new MyRectangle(10, 20, 110, 220);

            Assert.AreEqual(100, r.Width);
            Assert.AreEqual(200, r.Height);

            Assert.IsTrue(r.Contains(10, 20));
            Assert.IsTrue(r.Contains(109, 219));
            Assert.IsFalse(r.Contains(110, 220));

            Assert.AreEqual("[10,20,110,220]", r.ToString());
        }
    }
}
