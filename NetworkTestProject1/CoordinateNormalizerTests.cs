using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.Network;

namespace NetworkTestProject1
{
    [TestClass]
    public sealed class CoordinateNormalizerTests
    {
        [TestMethod]
        public void RoundTrip_Corners_WithinOnePixel()
        {
            CoordinateNormalizer.GetVirtualScreenBounds(out var left, out var top, out var width, out var height);

            // Test corners and center
            (int x, int y)[] points =
            {
                (left, top),
                (left + width - 1, top),
                (left, top + height - 1),
                (left + width - 1, top + height - 1),
                (left + width / 2, top + height / 2)
            };

            foreach (var p in points)
            {
                CoordinateNormalizer.ScreenToNormalized(p.x, p.y, out var nx, out var ny);
                CoordinateNormalizer.NormalizedToScreen(nx, ny, out var rx, out var ry);

                Assert.IsTrue(System.Math.Abs(rx - p.x) <= 1, $"X round-trip error too large for {p} -> {rx},{ry}");
                Assert.IsTrue(System.Math.Abs(ry - p.y) <= 1, $"Y round-trip error too large for {p} -> {rx},{ry}");
            }
        }
    }
}