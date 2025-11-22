namespace OmniMouse.Switching
{
    /// <summary>
    /// Policy for deciding when and how to switch between machines.
    /// </summary>
    public interface ISwitchPolicy
    {
        /// <summary>
        /// Evaluate whether a switch should occur based on mouse position.
        /// </summary>
        SwitchDecision Evaluate(MouseMoveContext context);

        /// <summary>
        /// Pixels from edge required to trigger switch.
        /// </summary>
        int EdgeThresholdPixels { get; set; }

        /// <summary>
        /// Milliseconds cooldown between switches.
        /// </summary>
        int CooldownMilliseconds { get; set; }

        /// <summary>
        /// Whether to block switches at monitor corners.
        /// </summary>
        bool BlockAtCorners { get; set; }

        /// <summary>
        /// Whether to use relative mouse movement (vs absolute).
        /// </summary>
        bool UseRelativeMovement { get; set; }
    }
}
