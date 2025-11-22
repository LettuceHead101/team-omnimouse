using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.Switching;
using System.Drawing;
using System;

namespace NetworkTestProject1.SwitchingTests
{
    [TestClass]
    public class IScreenTopologyTests
    {
        private class FakeScreenTopology : IScreenTopology
        {
            private readonly ScreenBounds _config;
            private Point _cursor;
            private bool _hasCursor;

            public FakeScreenTopology(ScreenBounds config, Point cursor, bool hasCursor = true)
            {
                _config = config ?? new ScreenBounds();
                _cursor = cursor;
                _hasCursor = hasCursor;
            }

            public ScreenBounds GetScreenConfiguration() => _config;

            public bool GetCursorPosition(out Point position)
            {
                if (!_hasCursor)
                {
                    position = Point.Empty;
                    return false;
                }

                position = _cursor;
                return true;
            }
        }

        [TestMethod]
        public void GetScreenConfiguration_ReturnsProvidedBounds()
        {
            var sb = new ScreenBounds();
            sb.DesktopBounds = new MyRectangle(0, 0, 1920, 1080);
            sb.PrimaryScreenBounds = new MyRectangle(0, 0, 1280, 720);
            sb.SensitivePoints = new Point[] { new Point(0, 0), new Point(1919, 1079) };

            var top = new FakeScreenTopology(sb, new Point(0, 0));
            var got = top.GetScreenConfiguration();

            Assert.AreEqual(sb.DesktopBounds.Left, got.DesktopBounds.Left);
            Assert.AreEqual(sb.DesktopBounds.Right, got.DesktopBounds.Right);
            Assert.AreEqual(sb.PrimaryScreenBounds.Left, got.PrimaryScreenBounds.Left);
            CollectionAssert.AreEqual(sb.SensitivePoints, got.SensitivePoints);
        }

        [TestMethod]
        public void GetCursorPosition_ReturnsTrueAndPosition_WhenAvailable()
        {
            var expected = new Point(100, 200);
            var top = new FakeScreenTopology(new ScreenBounds(), expected, hasCursor: true);

            var ok = top.GetCursorPosition(out var pos);
            Assert.IsTrue(ok);
            Assert.AreEqual(expected, pos);
        }

        [TestMethod]
        public void GetCursorPosition_ReturnsFalseAndEmpty_WhenUnavailable()
        {
            var top = new FakeScreenTopology(new ScreenBounds(), new Point(1, 1), hasCursor: false);

            var ok = top.GetCursorPosition(out var pos);
            Assert.IsFalse(ok);
            Assert.AreEqual(Point.Empty, pos);
        }
    }
}
