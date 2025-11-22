using System;
using System.Drawing;

namespace OmniMouse.Switching
{
    /// <summary>
    /// Maps pixel coordinates to 0-65535 universal range used by Windows SendInput.
    /// </summary>
    public class DefaultCoordinateMapper : ICoordinateMapper
    {
        private const int UniversalMax = 65535;

        public Point MapToUniversal(Point pixel, MyRectangle referenceBounds)
        {
            if (pixel.IsEmpty)
                return Point.Empty;

            int width = referenceBounds.Width;
            int height = referenceBounds.Height;

            if (width <= 0 || height <= 0)
                return Point.Empty;

            int x = (pixel.X - referenceBounds.Left) * UniversalMax / width;
            int y = (pixel.Y - referenceBounds.Top) * UniversalMax / height;

            return new Point(x, y);
        }

        public Point MapToPixel(Point universal, MyRectangle referenceBounds)
        {
            if (universal.IsEmpty)
                return Point.Empty;

            int width = referenceBounds.Width;
            int height = referenceBounds.Height;

            if (width <= 0 || height <= 0)
                return Point.Empty;

            int x = referenceBounds.Left + (universal.X * width / UniversalMax);
            int y = referenceBounds.Top + (universal.Y * height / UniversalMax);

            return new Point(x, y);
        }

        public MyRectangle GetReferenceBounds(bool isRelativeMode, bool isController, 
            MyRectangle desktopBounds, MyRectangle primaryBounds)
        {
            // In relative mode, always use desktop bounds
            if (isRelativeMode)
                return desktopBounds;

            // In absolute mode, controller uses desktop, others use primary
            return isController ? desktopBounds : primaryBounds;
        }
    }
}
