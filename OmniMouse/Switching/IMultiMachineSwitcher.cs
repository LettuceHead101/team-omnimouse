using System;
using System.Drawing;

namespace OmniMouse.Switching
{
    /// <summary>
    /// Event args for machine switch requests.
    /// </summary>
    public class MachineSwitchEventArgs : EventArgs
    {
        public string FromMachine { get; set; } = string.Empty;
        public string ToMachine { get; set; } = string.Empty;
        public Point RawCursorPoint { get; set; }
        public Point UniversalCursorPoint { get; set; }
        public SwitchReason Reason { get; set; }
        public Direction Direction { get; set; }

        public MachineSwitchEventArgs(string from, string to, Point raw, Point universal, 
            SwitchReason reason, Direction direction)
        {
            FromMachine = from;
            ToMachine = to;
            RawCursorPoint = raw;
            UniversalCursorPoint = universal;
            Reason = reason;
            Direction = direction;
        }
    }

    /// <summary>
    /// Main orchestrator for multi-machine switching.
    /// Integrates screen topology, machine layout, policy evaluation, and network coordination.
    /// </summary>
    public interface IMultiMachineSwitcher
    {
        /// <summary>
        /// Raised when a switch should occur.
        /// </summary>
        event EventHandler<MachineSwitchEventArgs>? SwitchRequested;

        /// <summary>
        /// Start monitoring for switches.
        /// </summary>
        void Start();

        /// <summary>
        /// Stop monitoring.
        /// </summary>
        void Stop();

        /// <summary>
        /// Update the machine matrix/layout.
        /// </summary>
        void UpdateMatrix(string[] machines, bool oneRow = true, bool wrapAround = false);

        /// <summary>
        /// Set which machine is currently active.
        /// </summary>
        void SetActiveMachine(string name);

        /// <summary>
        /// Process a mouse move event (called by input hook).
        /// </summary>
        void OnMouseMove(int x, int y);

        /// <summary>
        /// Get current screen configuration.
        /// </summary>
        ScreenBounds GetScreenBounds();
    }
}
