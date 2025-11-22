using System;
using System.Drawing;

namespace OmniMouse.Switching
{
    /// <summary>
    /// Reason why a switch decision was made or denied.
    /// </summary>
    public enum SwitchReason
    {
        None,
        EdgeLeft,
        EdgeRight,
        EdgeTop,
        EdgeBottom,
        CornerBlocked,
        CooldownActive,
        FullscreenBlocked,
        NoNeighbor
    }

    /// <summary>
    /// Context for evaluating switch conditions.
    /// </summary>
    public class MouseMoveContext
    {
        public DateTime Timestamp { get; set; }
        public Point RawPixel { get; set; }
        public MyRectangle DesktopBounds { get; set; }
        public MyRectangle PrimaryBounds { get; set; }
        public string CurrentMachine { get; set; } = string.Empty;
        public bool IsController { get; set; }
        public Point[] SensitivePoints { get; set; } = Array.Empty<Point>();
    }

    /// <summary>
    /// Result of switch evaluation.
    /// </summary>
    public class SwitchDecision
    {
        public bool ShouldSwitch { get; set; }
        public string? TargetMachine { get; set; }
        public SwitchReason Reason { get; set; }
        public Point UniversalPoint { get; set; }
        public Direction Direction { get; set; }

        public static SwitchDecision NoSwitch(SwitchReason reason) =>
            new SwitchDecision { ShouldSwitch = false, Reason = reason };

        public static SwitchDecision Switch(string target, SwitchReason reason, Point universalPoint, Direction dir) =>
            new SwitchDecision 
            { 
                ShouldSwitch = true, 
                TargetMachine = target, 
                Reason = reason, 
                UniversalPoint = universalPoint,
                Direction = dir
            };
    }
}
