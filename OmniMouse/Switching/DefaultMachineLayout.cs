using System;
using System.Linq;

namespace OmniMouse.Switching
{
    /// <summary>
    /// Machine layout implementation supporting up to 4 machines in either
    /// 1-row or 2x2 configuration
    /// Matrix positions: [0] [1]
    ///                   [2] [3]
    /// </summary>
    public class DefaultMachineLayout : IMachineLayout
    {
        private const int MaxMachines = 4;
        private string[] _machines = new string[MaxMachines];
        private string _currentMachine = string.Empty;

        public string CurrentMachine
        {
            get => _currentMachine;
            set => _currentMachine = value ?? string.Empty;
        }

        public string[] Machines => _machines;

        public bool IsOneRow { get; set; } = true;

        public bool EnableWrapAround { get; set; } = false;

        public DefaultMachineLayout()
        {
            for (int i = 0; i < MaxMachines; i++)
                _machines[i] = string.Empty;
        }

        public DefaultMachineLayout(string[] machineNames, bool oneRow = true, bool wrapAround = false)
        {
            if (machineNames == null) throw new ArgumentNullException(nameof(machineNames));
            
            _machines = new string[MaxMachines];
            for (int i = 0; i < MaxMachines; i++)
            {
                _machines[i] = i < machineNames.Length ? (machineNames[i] ?? string.Empty) : string.Empty;
            }

            IsOneRow = oneRow;
            EnableWrapAround = wrapAround;
        }

        public string? GetNeighbor(Direction direction, string currentMachine)
        {
            if (string.IsNullOrWhiteSpace(currentMachine))
                return null;

            return direction switch
            {
                Direction.Right => GetRightNeighbor(currentMachine),
                Direction.Left => GetLeftNeighbor(currentMachine),
                Direction.Up => GetUpNeighbor(currentMachine),
                Direction.Down => GetDownNeighbor(currentMachine),
                _ => null
            };
        }

        private string? GetRightNeighbor(string current)
        {
            if (IsOneRow)
            {
                // Linear layout: scan right
                for (int i = 0; i < MaxMachines; i++)
                {
                    if (current.Trim().Equals(_machines[i], StringComparison.OrdinalIgnoreCase))
                    {
                        // Look for next non-empty machine
                        for (int j = i + 1; j < MaxMachines; j++)
                        {
                            if (!string.IsNullOrWhiteSpace(_machines[j]))
                                return _machines[j];
                        }

                        // Wrap around if enabled
                        if (EnableWrapAround)
                        {
                            for (int j = 0; j < i; j++)
                            {
                                if (!string.IsNullOrWhiteSpace(_machines[j]))
                                    return _machines[j];
                            }
                        }
                        break;
                    }
                }
            }
            else
            {
                // 2x2 grid: [0][1]  Move right within row
                //           [2][3]
                if (current.Trim().Equals(_machines[0], StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_machines[1]))
                    return _machines[1];
                
                if (current.Trim().Equals(_machines[2], StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_machines[3]))
                    return _machines[3];

                // Wrap around horizontally
                if (EnableWrapAround)
                {
                    if (current.Trim().Equals(_machines[1], StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_machines[0]))
                        return _machines[0];
                    
                    if (current.Trim().Equals(_machines[3], StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_machines[2]))
                        return _machines[2];
                }
            }

            return null;
        }

        private string? GetLeftNeighbor(string current)
        {
            if (IsOneRow)
            {
                // Linear layout: scan left
                for (int i = MaxMachines - 1; i >= 0; i--)
                {
                    if (current.Trim().Equals(_machines[i], StringComparison.OrdinalIgnoreCase))
                    {
                        for (int j = i - 1; j >= 0; j--)
                        {
                            if (!string.IsNullOrWhiteSpace(_machines[j]))
                                return _machines[j];
                        }

                        if (EnableWrapAround)
                        {
                            for (int j = MaxMachines - 1; j > i; j--)
                            {
                                if (!string.IsNullOrWhiteSpace(_machines[j]))
                                    return _machines[j];
                            }
                        }
                        break;
                    }
                }
            }
            else
            {
                // 2x2 grid
                if (current.Trim().Equals(_machines[1], StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_machines[0]))
                    return _machines[0];
                
                if (current.Trim().Equals(_machines[3], StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_machines[2]))
                    return _machines[2];

                if (EnableWrapAround)
                {
                    if (current.Trim().Equals(_machines[0], StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_machines[1]))
                        return _machines[1];
                    
                    if (current.Trim().Equals(_machines[2], StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_machines[3]))
                        return _machines[3];
                }
            }

            return null;
        }

        private string? GetUpNeighbor(string current)
        {
            if (IsOneRow)
                return null; // No vertical movement in 1-row layout

            // 2x2 grid: move from bottom row to top row
            if (current.Trim().Equals(_machines[2], StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_machines[0]))
                return _machines[0];
            
            if (current.Trim().Equals(_machines[3], StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_machines[1]))
                return _machines[1];

            // Wrap around vertically
            if (EnableWrapAround)
            {
                if (current.Trim().Equals(_machines[0], StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_machines[2]))
                    return _machines[2];
                
                if (current.Trim().Equals(_machines[1], StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_machines[3]))
                    return _machines[3];
            }

            return null;
        }

        private string? GetDownNeighbor(string current)
        {
            if (IsOneRow)
                return null;

            // 2x2 grid: move from top row to bottom row
            if (current.Trim().Equals(_machines[0], StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_machines[2]))
                return _machines[2];
            
            if (current.Trim().Equals(_machines[1], StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_machines[3]))
                return _machines[3];

            if (EnableWrapAround)
            {
                if (current.Trim().Equals(_machines[2], StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_machines[0]))
                    return _machines[0];
                
                if (current.Trim().Equals(_machines[3], StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_machines[1]))
                    return _machines[1];
            }

            return null;
        }
    }
}
