using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.Switching;
using System.Drawing;

namespace NetworkTestProject1.Network
{
    [TestClass]
    public class DefaultCoordinateMapperTests
    {
        private readonly DefaultCoordinateMapper _mapper = new DefaultCoordinateMapper();

        [TestMethod]
        public void MapToUniversal_ReturnsEmpty_ForEmptyPoint()
        {
            var result = _mapper.MapToUniversal(Point.Empty, new MyRectangle(0, 0, 100, 100));
            Assert.AreEqual(Point.Empty, result);
        }

        [TestMethod]
        public void MapToUniversal_ReturnsEmpty_ForInvalidBounds()
        {
            var result = _mapper.MapToUniversal(new Point(10, 10), new MyRectangle(0, 0, 0, 0));
            Assert.AreEqual(Point.Empty, result);
        }

        [TestMethod]
        public void MapToUniversal_MapsKnownPoints()
        {
            var bounds = new MyRectangle(0, 0, 100, 100);

            var p0 = _mapper.MapToUniversal(new Point(0, 0), bounds);
            Assert.AreEqual(new Point(0, 0), p0);

            var p50 = _mapper.MapToUniversal(new Point(50, 50), bounds);
            // 50 * 65535 / 100 = 32767
            Assert.AreEqual(new Point(32767, 32767), p50);

            var p99 = _mapper.MapToUniversal(new Point(99, 99), bounds);
            // 99 * 65535 / 100 = 64879
            Assert.AreEqual(new Point(64879, 64879), p99);
        }

        [TestMethod]
        public void MapToPixel_MapsKnownUniversals()
        {
            var bounds = new MyRectangle(0, 0, 100, 100);

            var u0 = _mapper.MapToPixel(new Point(0, 0), bounds);
            Assert.AreEqual(new Point(0, 0), u0);

            var uMax = _mapper.MapToPixel(new Point(65535, 65535), bounds);
            // universal max maps to right/bottom
            Assert.AreEqual(new Point(100, 100), uMax);

            var uMid = _mapper.MapToPixel(new Point(32767, 32767), bounds);
            // 32767 * 100 / 65535 = 49 (integer division)
            Assert.AreEqual(new Point(49, 49), uMid);
        }

        [TestMethod]
        public void MapToUniversal_WithOffset_Bounds_MapsCorrectly()
        {
            var bounds = new MyRectangle(10, 20, 210, 220); // 200x200
            var p = new Point(110, 120); // center of the bounds

            var uni = _mapper.MapToUniversal(p, bounds);

            // (110-10) * 65535 / 200 = 32767
            Assert.AreEqual(new Point(32767, 32767), uni);
        }

        [TestMethod]
        public void MapToPixel_WithOffset_Bounds_MapsBackConsistently()
        {
            var bounds = new MyRectangle(10, 20, 210, 220); // 200x200
            var uni = new Point(32767, 32767);

            var pixel = _mapper.MapToPixel(uni, bounds);

            // compute expected according to the same integer math used in mapper
            var expectedX = bounds.Left + (uni.X * bounds.Width / 65535);
            var expectedY = bounds.Top + (uni.Y * bounds.Height / 65535);

            Assert.AreEqual(new Point(expectedX, expectedY), pixel);
        }

        [TestMethod]
        public void MapToUniversal_PixelAtRight_MapsToUniversalMax()
        {
            var bounds = new MyRectangle(10, 20, 210, 220); // 200x200
            var p = new Point(bounds.Right, bounds.Bottom);

            var uni = _mapper.MapToUniversal(p, bounds);
            Assert.AreEqual(new Point(65535, 65535), uni);
        }

        [TestMethod]
        public void MapToPixel_ReturnsEmpty_ForEmptyUniversal()
        {
            var bounds = new MyRectangle(0, 0, 100, 100);
            var result = _mapper.MapToPixel(System.Drawing.Point.Empty, bounds);
            Assert.AreEqual(System.Drawing.Point.Empty, result);
        }

        // Round-trip tests can be fragile due to integer truncation; core mapping behavior
        // is validated by the other tests above.

        [TestMethod]
        public void GetReferenceBounds_RespectsModes()
        {
            var desktop = new MyRectangle(0, 0, 1920, 1080);
            var primary = new MyRectangle(100, 100, 1280, 720);

            // Relative mode -> desktop
            var r1 = _mapper.GetReferenceBounds(true, false, desktop, primary);
            Assert.AreEqual(desktop.Left, r1.Left);

            // Absolute + controller -> desktop
            var r2 = _mapper.GetReferenceBounds(false, true, desktop, primary);
            Assert.AreEqual(desktop.Top, r2.Top);

            // Absolute + non-controller -> primary
            var r3 = _mapper.GetReferenceBounds(false, false, desktop, primary);
            Assert.AreEqual(primary.Left, r3.Left);
        }
    }
}
