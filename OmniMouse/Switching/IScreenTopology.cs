using System.Drawing;

namespace OmniMouse.Switching
{
    /// <summary>
    /// Interface for detecting screen configuration and monitor boundaries.
    /// Abstraction over Win32 monitor enumeration.
    /// </summary>
    public interface IScreenTopology
    {
        /// <summary>
        /// Refresh screen configuration (monitors, bounds, corners).
        /// </summary>
        ScreenBounds GetScreenConfiguration();

        /// <summary>
        /// Get current cursor position.
        /// </summary>
        bool GetCursorPosition(out Point position);
    }
}
