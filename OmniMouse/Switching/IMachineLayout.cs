using System;

namespace OmniMouse.Switching
{
    public enum Direction
    {
        Left,
        Right,
        Up,
        Down
    }

    /// <summary>
    /// Defines the layout of machines and their relative positions.
    /// Supports both 1-row (linear) and 2x2 grid layouts with optional wraparound.
    /// </summary>
    public interface IMachineLayout
    {
        /// <summary>
        /// Currently active machine name.
        /// </summary>
        string CurrentMachine { get; set; }

        /// <summary>
        /// All machine names in the layout.
        /// </summary>
        string[] Machines { get; }

        /// <summary>
        /// Get the neighbor machine in the specified direction.
        /// Returns null if no neighbor exists.
        /// </summary>
        string? GetNeighbor(Direction direction, string currentMachine);

        /// <summary>
        /// Whether the layout is a single row (linear).
        /// </summary>
        bool IsOneRow { get; set; }

        /// <summary>
        /// Whether to wrap around at edges (circle mode).
        /// </summary>
        bool EnableWrapAround { get; set; }
    }
}
