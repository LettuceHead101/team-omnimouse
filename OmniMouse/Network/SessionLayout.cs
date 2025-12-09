using System;
using System.Collections.Generic;
using System.Linq;
using OmniMouse.Switching;

namespace OmniMouse.Network
{
    /// <summary>
    /// Represents a machine connected to the session with its position in the layout.
    /// </summary>
    public class ConnectedMachine
    {
        public string MachineId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int Position { get; set; } = -1;  // -1 = unassigned (legacy linear position)
        public int GridX { get; set; } = -1;  // -1 = unassigned
        public int GridY { get; set; } = -1;  // -1 = unassigned
        public bool IsLocal { get; set; }
        public bool IsPositioned => Position >= 0 || (GridX >= 0 && GridY >= 0);

        public ConnectedMachine Clone()
        {
            return new ConnectedMachine
            {
                MachineId = MachineId,
                DisplayName = DisplayName,
                Position = Position,
                GridX = GridX,
                GridY = GridY,
                IsLocal = IsLocal
            };
        }
    }

    /// <summary>
    /// Represents the synchronized layout of all machines in the session.
    /// Supports both linear (Position) and 2x2 grid (GridX, GridY) layouts.
    /// </summary>
    public class SessionLayout
    {
        public List<ConnectedMachine> Machines { get; set; } = new();
        public string AuthorityMachineId { get; set; } = string.Empty;
        public DateTime LastUpdateTimestamp { get; set; } = DateTime.UtcNow;
        public int GridWidth { get; set; } = 2;  // Default 2x2 grid
        public int GridHeight { get; set; } = 2;

        /// <summary>
        /// Gets machines ordered by position.
        /// </summary>
        public IEnumerable<ConnectedMachine> OrderedMachines => 
            Machines.Where(m => m.IsPositioned).OrderBy(m => m.Position);

        /// <summary>
        /// Gets the machine at the specified position, or null if empty.
        /// </summary>
        public ConnectedMachine? GetMachineAtPosition(int position)
        {
            return Machines.FirstOrDefault(m => m.Position == position);
        }

        /// <summary>
        /// Gets the machine at the specified grid coordinates, or null if empty.
        /// </summary>
        public ConnectedMachine? GetMachineAtGridPosition(int gridX, int gridY)
        {
            return Machines.FirstOrDefault(m => m.GridX == gridX && m.GridY == gridY);
        }

        /// <summary>
        /// Gets the neighbor machine in the specified direction from the given machine.
        /// Returns null if no neighbor exists or slot is empty.
        /// </summary>
        public ConnectedMachine? GetNeighbor(ConnectedMachine machine, Direction direction)
        {
            if (machine == null || machine.GridX < 0 || machine.GridY < 0)
                return null;

            int targetX = machine.GridX;
            int targetY = machine.GridY;

            switch (direction)
            {
                case Direction.Left:
                    targetX--;
                    break;
                case Direction.Right:
                    targetX++;
                    break;
                case Direction.Up:
                    targetY--;
                    break;
                case Direction.Down:
                    targetY++;
                    break;
            }

            // Check bounds
            if (targetX < 0 || targetX >= GridWidth || targetY < 0 || targetY >= GridHeight)
                return null;

            return GetMachineAtGridPosition(targetX, targetY);
        }

        /// <summary>
        /// Checks if a position is available.
        /// </summary>
        public bool IsPositionAvailable(int position)
        {
            return !Machines.Any(m => m.Position == position);
        }

        /// <summary>
        /// Gets the next available position.
        /// </summary>
        public int GetNextAvailablePosition()
        {
            if (Machines.Count == 0) return 0;
            
            var usedPositions = Machines.Where(m => m.IsPositioned).Select(m => m.Position).ToHashSet();
            int pos = 0;
            while (usedPositions.Contains(pos))
                pos++;
            return pos;
        }

        /// <summary>
        /// Updates or adds a machine to the layout.
        /// </summary>
        public void UpdateMachine(ConnectedMachine machine)
        {
            var existing = Machines.FirstOrDefault(m => m.MachineId == machine.MachineId);
            if (existing != null)
            {
                existing.Position = machine.Position;
                existing.DisplayName = machine.DisplayName;
            }
            else
            {
                Machines.Add(machine.Clone());
            }
            LastUpdateTimestamp = DateTime.UtcNow;
        }

        /// <summary>
        /// Removes a machine from the layout.
        /// </summary>
        public void RemoveMachine(string machineId)
        {
            Machines.RemoveAll(m => m.MachineId == machineId);
            LastUpdateTimestamp = DateTime.UtcNow;
        }

        /// <summary>
        /// Creates a deep copy of the layout.
        /// </summary>
        public SessionLayout Clone()
        {
            return new SessionLayout
            {
                Machines = Machines.Select(m => m.Clone()).ToList(),
                AuthorityMachineId = AuthorityMachineId,
                LastUpdateTimestamp = LastUpdateTimestamp
            };
        }

        /// <summary>
        /// Gets the array of machine IDs ordered by position for MultiMachineSwitcher.
        /// </summary>
        public string[] GetOrderedMachineIds()
        {
            return OrderedMachines.Select(m => m.MachineId).ToArray();
        }
    }
}
