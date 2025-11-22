using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OmniMouse.Switching
{
    /// <summary>
    /// Win32 implementation of screen topology detection.
    /// </summary>
    public class Win32ScreenTopology : IScreenTopology
    {
        private MyRectangle _newDesktopBounds;
        private MyRectangle _newPrimaryScreenBounds;
        private readonly List<Point> _sensitivePoints = new();

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfoEx
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        public ScreenBounds GetScreenConfiguration()
        {
            _newDesktopBounds = new MyRectangle();
            _newPrimaryScreenBounds = new MyRectangle();
            _sensitivePoints.Clear();

            // Initialize with primary screen
            var primary = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            _newDesktopBounds.Left = _newPrimaryScreenBounds.Left = primary.Left;
            _newDesktopBounds.Top = _newPrimaryScreenBounds.Top = primary.Top;
            _newDesktopBounds.Right = _newPrimaryScreenBounds.Right = primary.Right;
            _newDesktopBounds.Bottom = _newPrimaryScreenBounds.Bottom = primary.Bottom;

            // Enumerate all monitors
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonitorEnumProc, IntPtr.Zero);

            return new ScreenBounds
            {
                DesktopBounds = _newDesktopBounds,
                PrimaryScreenBounds = _newPrimaryScreenBounds,
                SensitivePoints = _sensitivePoints.ToArray()
            };
        }

        private bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
        {
            MonitorInfoEx mi = new MonitorInfoEx();
            mi.cbSize = Marshal.SizeOf(mi);
            
            if (!GetMonitorInfo(hMonitor, ref mi))
                return true;

            try
            {
                // Get DPI for logging (optional)
                GetDpiForMonitor(hMonitor, 0, out uint dpiX, out uint dpiY);
                Console.WriteLine($"[Monitor] ({mi.rcMonitor.Left}, {mi.rcMonitor.Top}, {mi.rcMonitor.Right}, {mi.rcMonitor.Bottom}). DPI: ({dpiX}, {dpiY})");
            }
            catch
            {
                // Ignore DPI errors on older systems
            }

            // Detect primary screen (origin at 0,0)
            if (mi.rcMonitor.Left == 0 && mi.rcMonitor.Top == 0 && 
                mi.rcMonitor.Right != 0 && mi.rcMonitor.Bottom != 0)
            {
                _newPrimaryScreenBounds.Left = mi.rcMonitor.Left;
                _newPrimaryScreenBounds.Top = mi.rcMonitor.Top;
                _newPrimaryScreenBounds.Right = mi.rcMonitor.Right;
                _newPrimaryScreenBounds.Bottom = mi.rcMonitor.Bottom;
            }
            else
            {
                // Expand desktop bounds to include all monitors
                if (mi.rcMonitor.Left < _newDesktopBounds.Left)
                    _newDesktopBounds.Left = mi.rcMonitor.Left;
                if (mi.rcMonitor.Top < _newDesktopBounds.Top)
                    _newDesktopBounds.Top = mi.rcMonitor.Top;
                if (mi.rcMonitor.Right > _newDesktopBounds.Right)
                    _newDesktopBounds.Right = mi.rcMonitor.Right;
                if (mi.rcMonitor.Bottom > _newDesktopBounds.Bottom)
                    _newDesktopBounds.Bottom = mi.rcMonitor.Bottom;
            }

            // Track corner points for blocking mouse at screen edges
            _sensitivePoints.Add(new Point(mi.rcMonitor.Left, mi.rcMonitor.Top));
            _sensitivePoints.Add(new Point(mi.rcMonitor.Right, mi.rcMonitor.Top));
            _sensitivePoints.Add(new Point(mi.rcMonitor.Right, mi.rcMonitor.Bottom));
            _sensitivePoints.Add(new Point(mi.rcMonitor.Left, mi.rcMonitor.Bottom));

            return true;
        }

        public bool GetCursorPosition(out Point position)
        {
            if (GetCursorPos(out POINT p))
            {
                position = new Point(p.X, p.Y);
                return true;
            }
            position = Point.Empty;
            return false;
        }
    }
}
