using System;
using System.Drawing;

namespace OmniMouse.Switching
{
    /// <summary>
    /// Represents the bounds of monitors/screens for edge detection.
    /// </summary>
    public struct MyRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;

        public MyRectangle(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public bool Contains(int x, int y)
        {
            return x >= Left && x < Right && y >= Top && y < Bottom;
        }

        public override string ToString() => $"[{Left},{Top},{Right},{Bottom}]";
    }

    /// <summary>
    /// Complete screen configuration including primary and desktop bounds.
    /// </summary>
    public class ScreenBounds
    {
        public MyRectangle DesktopBounds { get; set; }
        public MyRectangle PrimaryScreenBounds { get; set; }
        public Point[] SensitivePoints { get; set; } = Array.Empty<Point>();

        public ScreenBounds()
        {
            DesktopBounds = new MyRectangle();
            PrimaryScreenBounds = new MyRectangle();
        }
    }
}
