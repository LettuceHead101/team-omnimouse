using System.Drawing;

namespace OmniMouse.Switching
{
    /// <summary>
    /// Maps pixel coordinates to/from universal 0-65535 range.
    /// </summary>
    public interface ICoordinateMapper
    {
        /// <summary>
        /// Convert pixel coordinates to universal value (0-65535).
        /// </summary>
        Point MapToUniversal(Point pixel, MyRectangle referenceBounds);

        /// <summary>
        /// Convert universal coordinates back to pixels.
        /// </summary>
        Point MapToPixel(Point universal, MyRectangle referenceBounds);

        /// <summary>
        /// Get the appropriate reference bounds based on context (relative mode, machine role).
        /// </summary>
        MyRectangle GetReferenceBounds(bool isRelativeMode, bool isController, 
            MyRectangle desktopBounds, MyRectangle primaryBounds);
    }
}
