using System;
using System.Runtime.InteropServices;

namespace OmniMouse.Network
{
    internal static class CoordinateNormalizer
    {
        // SM_ system metrics indexes for virtual screen
        // These constants are passed to GetSystemMetrics to query the virtual desktop bounds.
        private const int SM_XVIRTUALSCREEN = 76; // left-most X coordinate of the virtual screen
        private const int SM_YVIRTUALSCREEN = 77; // top-most Y coordinate of the virtual screen
        private const int SM_CXVIRTUALSCREEN = 78; // total width of the virtual screen
        private const int SM_CYVIRTUALSCREEN = 79; // total height of the virtual screen

        // Import the Win32 GetSystemMetrics function to read system metrics such as virtual screen bounds.
        // The int return type maps to the native LONG/int used by GetSystemMetrics.
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        // Fills out the virtual desktop bounds: left, top, width and height.
        // Uses GetSystemMetrics with SM_XVIRTUALSCREEN/SM_YVIRTUALSCREEN/SM_CXVIRTUALSCREEN/SM_CYVIRTUALSCREEN.
        public static void GetVirtualScreenBounds(out int left, out int top, out int width, out int height)
        {
            // Query the left-most X coordinate of the combined (virtual) desktop.
            left = GetSystemMetrics(SM_XVIRTUALSCREEN);

            // Query the top-most Y coordinate of the combined (virtual) desktop.
            top = GetSystemMetrics(SM_YVIRTUALSCREEN);

            // Query the width (in pixels) of the combined (virtual) desktop.
            width = GetSystemMetrics(SM_CXVIRTUALSCREEN);

            // Query the height (in pixels) of the combined (virtual) desktop.
            height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            // Defensive fallback: ensure width is at least 1 to avoid division by zero.
            if (width <= 0) width = 1;

            // Defensive fallback: ensure height is at least 1 to avoid division by zero.
            if (height <= 0) height = 1;
        }

        // Convert absolute screen coords (x,y) into normalized coords in [0,1].
        // Convention chosen: bottom-left => (0,0), top-right => (1,1).
        // Note: Windows screen coordinates use top-left as origin, so Y is inverted during conversion.
        public static void ScreenToNormalized(int x, int y, out float nx, out float ny)
        {
            // Get the virtual desktop bounds for the current machine.
            GetVirtualScreenBounds(out var left, out var top, out var width, out var height);

            // Compute normalized X as fraction of the virtual desktop width.
            // (x - left) shifts the absolute X into the virtual-desktop local coordinate system.
            double dx = (x - left) / (double)width;

            // Compute normalized Y as fraction of the virtual desktop height.
            // (y - top) shifts the absolute Y into the virtual-desktop local coordinate system.
            double dy = (y - top) / (double)height;

            // Clamp dx to [0.0, 1.0] to avoid coordinates outside the virtual desktop due to rounding or off-screen events.
            dx = Math.Clamp(dx, 0.0, 1.0);

            // Clamp dy to [0.0, 1.0] for the same reason as above.
            dy = Math.Clamp(dy, 0.0, 1.0);

            // Cast normalized X to float for network serialization / API compatibility.
            nx = (float)dx;

            // Invert Y and cast to float: windows top-left origin -> we want bottom-left origin.
            // ny = 1.0 - dy ensures top-right of screen maps to (1,1) under our chosen convention.
            ny = (float)(1.0 - dy);
        }

        // Convert normalized coords back to absolute screen coords for this machine.
        // Accepts normalized values in [0,1] and outputs integer screen coordinates.
        public static void NormalizedToScreen(float nx, float ny, out int x, out int y)
        {
            // Re-read the virtual desktop bounds for the current machine.
            GetVirtualScreenBounds(out var left, out var top, out var width, out var height);

            // Clamp incoming normalized X to [0,1] to guard against malformed or noisy network input.
            var clampedX = Math.Clamp(nx, 0f, 1f);

            // Clamp incoming normalized Y to [0,1] for the same reason.
            var clampedY = Math.Clamp(ny, 0f, 1f);

            // Invert Y back to Windows top-left origin coordinate space.
            // If ny==0 (bottom) -> realY==1 -> maps to top of virtual desktop in normalized fraction before scaling.
            double realY = 1.0 - clampedY;

            // Compute integer X: left offset plus the fraction of (width - 1) to cover 0..(width-1) pixel indices.
            // Use Math.Round to reduce quantization bias when converting float fraction to integer pixel coordinate.
            x = left + (int)Math.Round(clampedX * (width - 1));

            // Compute integer Y similarly: top offset plus the fraction of (height - 1).
            y = top + (int)Math.Round(realY * (height - 1));
        }
    }
}