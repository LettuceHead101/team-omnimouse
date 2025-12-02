using System;
using System.Drawing;

namespace OmniMouse.Switching
{
    /// <summary>
    /// Maps pixel coordinates to 0-65535 universal range used by Windows SendInput.
    /// </summary>
    public class DefaultCoordinateMapper : ICoordinateMapper
    {
        private const int UniversalMax = 65535;//16 bit max value

        public Point MapToUniversal(Point pixel, MyRectangle referenceBounds)
        {
            //if (pixel.IsEmpty)
            //    return Point.Empty;

            int width = referenceBounds.Width;
            int height = referenceBounds.Height;

            if (width <= 0 || height <= 0)
                return new Point(0, 0);

            // 2. USE DOUBLE MATH to prevent rounding errors and overflow

            // We calculate relative to the reference bounds (Left/Top)

            double relativeX = pixel.X - referenceBounds.Left;

            double relativeY = pixel.Y - referenceBounds.Top;

            // 3. CLAMP to ensure we don't send illegal values (< 0 or > 65535)

            // if the mouse accidentally slips slightly outside the formal bounds.

            double xNorm = (relativeX * UniversalMax) / width;

            double yNorm = (relativeY * UniversalMax) / height;

            return new Point((int)Math.Round(xNorm), (int)Math.Round(yNorm));
        }

        public Point MapToPixel(Point universal, MyRectangle referenceBounds)
        {
            //if (universal.IsEmpty)
            //    return Point.Empty;

            int width = referenceBounds.Width;
            int height = referenceBounds.Height;

            if (width <= 0 || height <= 0)
                return new Point(0,0);

            // 2. REVERSE MAPPING using double and Rounding
            double xPixels = (universal.X * width) / UniversalMax;
            double yPixels = (universal.Y * height) / UniversalMax;

            // Add the offset (Left/Top) to place it correctly on the virtual desktop
            int finalX = referenceBounds.Left + (int)Math.Round(xPixels);
            int finalY = referenceBounds.Top + (int)Math.Round(yPixels);

            return new Point(finalX, finalY);
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
