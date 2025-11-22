using System;
using System.Drawing;

namespace OmniMouse.Switching
{
    /// <summary>
    /// Extracted from MachineStuff.MoveToMyNeighbourIfNeeded and Event.MouseEvent
    /// </summary>
    public class DefaultSwitchPolicy : ISwitchPolicy
    {
        private readonly IMachineLayout _layout;
        private readonly ICoordinateMapper _mapper;
        private DateTime _lastSwitchTime = DateTime.MinValue;

        private const int DefaultEdgeThreshold = 2; // SKIP_PIXELS
        private const int DefaultJumpPixels = 10; // JUMP_PIXELS
        private const int DefaultCooldown = 100; // milliseconds
        private const int CornerDetectionRadius = 100; // pixels

        public int EdgeThresholdPixels { get; set; } = DefaultEdgeThreshold;
        public int CooldownMilliseconds { get; set; } = DefaultCooldown;
        public bool BlockAtCorners { get; set; } = false;
        public bool UseRelativeMovement { get; set; } = true;

        public DefaultSwitchPolicy(IMachineLayout layout, ICoordinateMapper mapper)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public SwitchDecision Evaluate(MouseMoveContext context)
        {
            if (context == null)
                return SwitchDecision.NoSwitch(SwitchReason.None);

            // Check cooldown
            var elapsed = (DateTime.Now - _lastSwitchTime).TotalMilliseconds;
            if (elapsed < CooldownMilliseconds && elapsed >= 0)
                return SwitchDecision.NoSwitch(SwitchReason.CooldownActive);

            // Check if at screen corners (optional blocking)
            if (BlockAtCorners && IsNearCorner(context.RawPixel, context.SensitivePoints))
                return SwitchDecision.NoSwitch(SwitchReason.CornerBlocked);

            // Determine which bounds to use
            var bounds = UseRelativeMovement 
                ? context.DesktopBounds 
                : (context.IsController ? context.DesktopBounds : context.PrimaryBounds);

            int x = context.RawPixel.X;
            int y = context.RawPixel.Y;

            // Check edges and determine direction
            Direction? direction = null;
            Point jumpPoint = Point.Empty;

            if (x < bounds.Left + EdgeThresholdPixels)
            {
                direction = Direction.Left;
                jumpPoint = new Point(bounds.Right - DefaultJumpPixels, y);
            }
            else if (x >= bounds.Right - EdgeThresholdPixels)
            {
                direction = Direction.Right;
                jumpPoint = new Point(bounds.Left + DefaultJumpPixels, y);
            }
            else if (y < bounds.Top + EdgeThresholdPixels)
            {
                direction = Direction.Up;
                jumpPoint = new Point(x, bounds.Bottom - DefaultJumpPixels);
            }
            else if (y >= bounds.Bottom - EdgeThresholdPixels)
            {
                direction = Direction.Down;
                jumpPoint = new Point(x, bounds.Top + DefaultJumpPixels);
            }

            if (!direction.HasValue)
                return SwitchDecision.NoSwitch(SwitchReason.None);

            // Find neighbor machine
            string? neighbor = _layout.GetNeighbor(direction.Value, context.CurrentMachine);
            if (string.IsNullOrWhiteSpace(neighbor))
                return SwitchDecision.NoSwitch(SwitchReason.NoNeighbor);

            // Calculate universal coordinates for the target position
            var referenceBounds = _mapper.GetReferenceBounds(
                UseRelativeMovement, 
                context.IsController, 
                context.DesktopBounds, 
                context.PrimaryBounds);

            Point universalPoint = _mapper.MapToUniversal(jumpPoint, referenceBounds);

            // record successful switch time
            _lastSwitchTime = DateTime.Now;

            var reason = direction.Value switch
            {
                Direction.Left => SwitchReason.EdgeLeft,
                Direction.Right => SwitchReason.EdgeRight,
                Direction.Up => SwitchReason.EdgeTop,
                Direction.Down => SwitchReason.EdgeBottom,
                _ => SwitchReason.None
            };

            return SwitchDecision.Switch(neighbor, reason, universalPoint, direction.Value);
        }

        private bool IsNearCorner(Point position, Point[] corners)
        {
            if (corners == null || corners.Length == 0)
                return false;

            foreach (var corner in corners)
            {
                int dx = Math.Abs(corner.X - position.X);
                int dy = Math.Abs(corner.Y - position.Y);
                
                if (dx < CornerDetectionRadius && dy < CornerDetectionRadius)
                    return true;
            }

            return false;
        }
    }
}
