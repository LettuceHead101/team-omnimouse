using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.Switching;
using System.Drawing;

namespace NetworkTestProject1.Network
{
    [TestClass]
    public class ICoordinateMapperTests
    {
        private ICoordinateMapper CreateMapper() => new DefaultCoordinateMapper();

        [TestMethod]
        public void Implementation_IsAssignableToInterface()
        {
            var mapper = CreateMapper();
            Assert.IsNotNull(mapper);
            Assert.IsInstanceOfType(mapper, typeof(ICoordinateMapper));
        }

        [TestMethod]
        public void MapToUniversal_ReturnsEmpty_ForEmptyPoint()
        {
            ICoordinateMapper mapper = CreateMapper();
            var result = mapper.MapToUniversal(Point.Empty, new MyRectangle(0, 0, 100, 100));
            Assert.AreEqual(Point.Empty, result);
        }

        [TestMethod]
        public void MapToUniversal_ReturnsEmpty_ForInvalidBounds()
        {
            ICoordinateMapper mapper = CreateMapper();
            var result = mapper.MapToUniversal(new Point(5, 5), new MyRectangle(0, 0, 0, 0));
            Assert.AreEqual(Point.Empty, result);
        }

        [TestMethod]
        public void Map_BoundaryValues_MapToExpectedExtremes()
        {
            ICoordinateMapper mapper = CreateMapper();
            var bounds = new MyRectangle(10, 20, 110, 120); // 100x100

            var leftTopUni = mapper.MapToUniversal(new Point(bounds.Left, bounds.Top), bounds);
            Assert.AreEqual(new Point(0, 0), leftTopUni);

            var rightBottomUni = mapper.MapToUniversal(new Point(bounds.Right, bounds.Bottom), bounds);
            Assert.AreEqual(new Point(65535, 65535), rightBottomUni);

            // Avoid using Point.Empty (0,0) sentinel in MapToPixel; use 1,1 which maps to the same pixel
            // for integer math on small widths.
            var backLeftTop = mapper.MapToPixel(new Point(1, 1), bounds);
            Assert.AreEqual(new Point(bounds.Left, bounds.Top), backLeftTop);

            var backRightBottom = mapper.MapToPixel(new Point(65535, 65535), bounds);
            Assert.AreEqual(new Point(bounds.Right, bounds.Bottom), backRightBottom);
        }

        [TestMethod]
        public void Map_RoundTrip_IsReasonablyClose()
        {
            ICoordinateMapper mapper = CreateMapper();
            var bounds = new MyRectangle(0, 0, 320, 240);

            var samples = new[] { new Point(0, 0), new Point(160, 120), new Point(319, 239) };
            foreach (var s in samples)
            {
                var uni = mapper.MapToUniversal(s, bounds);
                var pixel = mapper.MapToPixel(uni, bounds);

                // allow small integer truncation differences
                Assert.IsTrue(System.Math.Abs(pixel.X - s.X) <= 2, "X should be within 2 pixels of original");
                Assert.IsTrue(System.Math.Abs(pixel.Y - s.Y) <= 2, "Y should be within 2 pixels of original");
            }
        }

        [TestMethod]
        public void GetReferenceBounds_ReturnsCorrectChoice()
        {
            ICoordinateMapper mapper = CreateMapper();
            var desktop = new MyRectangle(0, 0, 1920, 1080);
            var primary = new MyRectangle(100, 100, 1280, 720);

            var r1 = mapper.GetReferenceBounds(true, false, desktop, primary);
            Assert.AreEqual(desktop.Left, r1.Left);

            var r2 = mapper.GetReferenceBounds(false, true, desktop, primary);
            Assert.AreEqual(desktop.Left, r2.Left);

            var r3 = mapper.GetReferenceBounds(false, false, desktop, primary);
            Assert.AreEqual(primary.Left, r3.Left);
        }
    }
}
