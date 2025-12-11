using System;
using System.Collections.Generic;
using System.Linq;

namespace OmniMouse.Network
{
    /// <summary>
    /// Represents a machine connected to the session with its position in the layout.
    /// </summary>
    public class ConnectedMachine
    {
        public string MachineId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int Position { get; set; } = -1;  // -1 = unassigned
        public bool IsLocal { get; set; }
        public bool IsPositioned => Position >= 0;

        public ConnectedMachine Clone()
        {
            return new ConnectedMachine
            {
                MachineId = MachineId,
                DisplayName = DisplayName,
                Position = Position,
                IsLocal = IsLocal
            };
        }
    }

    /// <summary>
    /// Represents the synchronized layout of all machines in the session.
    /// </summary>
    public class SessionLayout
    {
        public List<ConnectedMachine> Machines { get; set; } = new();
        public string AuthorityMachineId { get; set; } = string.Empty;
        public DateTime LastUpdateTimestamp { get; set; } = DateTime.UtcNow;

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

        /// <summary>
        /// Gets the neighbor machine in the specified direction.
        /// For linear layouts, only left/right are supported.
        /// </summary>
        public ConnectedMachine? GetNeighbor(ConnectedMachine machine, OmniMouse.Switching.Direction direction)
        {
            if (!machine.IsPositioned) return null;

            // For linear (1D) layout, only left/right make sense
            switch (direction)
            {
                case OmniMouse.Switching.Direction.Right:
                    return GetMachineAtPosition(machine.Position + 1);
                    
                case OmniMouse.Switching.Direction.Left:
                    return GetMachineAtPosition(machine.Position - 1);
                    
                case OmniMouse.Switching.Direction.Up:
                case OmniMouse.Switching.Direction.Down:
                    // For 1D layouts, up/down don't have neighbors
                    // For 2D grid layouts, this would need grid dimensions
                    return null;
                    
                default:
                    return null;
            }
        }
    }
}
