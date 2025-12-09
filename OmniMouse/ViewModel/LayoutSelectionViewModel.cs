namespace OmniMouse.ViewModel
{
    using System;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Windows.Input;
    using OmniMouse.MVVM;
    using OmniMouse.Network;

    /// <summary>
    /// ViewModel for the layout selection dialog.
    /// Manages the visual representation of connected machines and their positions.
    /// </summary>
    public class LayoutSelectionViewModel : ViewModelBase
    {
        private readonly LayoutCoordinator _coordinator;
        private readonly string _localMachineId;
        private ObservableCollection<MachinePositionItem> _machines = new();
        private ObservableCollection<MachinePositionItem> _gridSlots = new();
        private ObservableCollection<MachinePositionItem> _unpositionedMachines = new();
        private bool _isLayoutComplete;
        private string _statusMessage = "Arrange your PC position using drag & drop";

        public ObservableCollection<MachinePositionItem> Machines
        {
            get => _machines;
            set => SetProperty(ref _machines, value);
        }

        public ObservableCollection<MachinePositionItem> GridSlots
        {
            get => _gridSlots;
            set => SetProperty(ref _gridSlots, value);
        }

        public ObservableCollection<MachinePositionItem> UnpositionedMachines
        {
            get => _unpositionedMachines;
            set => SetProperty(ref _unpositionedMachines, value);
        }

        public bool IsLayoutComplete
        {
            get => _isLayoutComplete;
            private set => SetProperty(ref _isLayoutComplete, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public int LocalPosition
        {
            get
            {
                var localMachine = Machines.FirstOrDefault(m => m.IsLocal);
                return localMachine?.Position ?? -1;
            }
        }

        public ICommand ConfirmLayoutCommand { get; }
        public ICommand CancelCommand { get; }

        public event EventHandler? LayoutConfirmed;
        public event EventHandler? Cancelled;

        public LayoutSelectionViewModel(LayoutCoordinator coordinator, string localMachineId)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _localMachineId = localMachineId ?? throw new ArgumentNullException(nameof(localMachineId));

            ConfirmLayoutCommand = new RelayCommand(_ => OnConfirmLayout(), _ => IsLayoutComplete);
            CancelCommand = new RelayCommand(_ => OnCancel());

            // Subscribe to layout changes
            _coordinator.LayoutChanged += OnLayoutChanged;

            // Load initial layout
            RefreshMachines();
        }

        private void RefreshMachines()
        {
            var layout = _coordinator.CurrentLayout;

            Machines.Clear();
            GridSlots.Clear();
            UnpositionedMachines.Clear();

            // Create 2x2 grid slots (4 total positions)
            for (int y = 0; y < layout.GridHeight; y++)
            {
                for (int x = 0; x < layout.GridWidth; x++)
                {
                    var machineAtPos = layout.GetMachineAtGridPosition(x, y);
                    
                    if (machineAtPos != null)
                    {
                        // This slot has a machine - show the machine in grid
                        var item = new MachinePositionItem
                        {
                            MachineId = machineAtPos.MachineId,
                            DisplayName = machineAtPos.DisplayName,
                            GridX = x,
                            GridY = y,
                            Position = machineAtPos.Position,
                            IsLocal = machineAtPos.IsLocal,
                            IsPositioned = true,
                            IsEmptySlot = false,
                        };
                        GridSlots.Add(item);
                        Machines.Add(item);
                    }
                    else
                    {
                        // Empty slot - show as drop target
                        var emptySlot = new MachinePositionItem
                        {
                            MachineId = $"empty_{x}_{y}",
                            DisplayName = $"[{x},{y}]",
                            GridX = x,
                            GridY = y,
                            Position = -1,
                            IsLocal = false,
                            IsPositioned = false,
                            IsEmptySlot = true,
                        };
                        GridSlots.Add(emptySlot);
                    }
                }
            }

            // Add unpositioned machines to the available list
            foreach (var machine in layout.Machines.Where(m => !m.IsPositioned))
            {
                var item = new MachinePositionItem
                {
                    MachineId = machine.MachineId,
                    DisplayName = machine.DisplayName,
                    GridX = -1,
                    GridY = -1,
                    Position = -1,
                    IsLocal = machine.IsLocal,
                    IsPositioned = false,
                    IsEmptySlot = false,
                };
                UnpositionedMachines.Add(item);
                Machines.Add(item);
            }

            UpdateStatus();
        }

        private void OnLayoutChanged(object? sender, LayoutChangedEventArgs e)
        {
            RefreshMachines();
        }

        private void UpdateStatus()
        {
            IsLayoutComplete = UnpositionedMachines.Count == 0;

            if (IsLayoutComplete)
            {
                StatusMessage = "✓ Layout complete! Click Confirm to continue.";
            }
            else
            {
                StatusMessage = $"Drag machines from Available list to grid ({UnpositionedMachines.Count} unassigned)";
            }
        }

        /// <summary>
        /// Called when user drags any machine to a new grid position.
        /// </summary>
        public void MoveMachine(string machineId, int newPosition)
        {
            // Legacy method - convert linear position to grid coordinates
            // For 2x2 grid: position 0 = [0,0], 1 = [1,0], 2 = [0,1], 3 = [1,1]
            int gridX = newPosition % 2;
            int gridY = newPosition / 2;
            MoveMachineToGrid(machineId, gridX, gridY);
        }

        /// <summary>
        /// Called when user drags any machine to a new grid position.
        /// </summary>
        public void MoveMachineToGrid(string machineId, int gridX, int gridY)
        {
            var machine = Machines.FirstOrDefault(m => m.MachineId == machineId);
            if (machine == null || machine.MachineId.StartsWith("empty_"))
            {
                return;
            }

            // Check if position is already occupied
            var conflict = Machines.FirstOrDefault(m => m.GridX == gridX && m.GridY == gridY && m.MachineId != machineId && !m.IsEmptySlot);
            if (conflict != null)
            {
                StatusMessage = $"Position [{gridX},{gridY}] is occupied by {conflict.DisplayName}";
                return;
            }

            // Update the machine's grid position via coordinator
            _coordinator.SetMachineGridPosition(machineId, gridX, gridY);

            // Refresh will happen via LayoutChanged event
        }

        /// <summary>
        /// Called when user drags their local machine to a new position (backward compatibility).
        /// </summary>
        public void MoveLocalMachine(int newPosition)
        {
            var localMachine = Machines.FirstOrDefault(m => m.IsLocal);
            if (localMachine != null)
            {
                MoveMachine(localMachine.MachineId, newPosition);
            }
        }

        /// <summary>
        /// Gets the next available position for the local machine.
        /// </summary>
        public int GetSuggestedPosition()
        {
            return _coordinator.CurrentLayout.GetNextAvailablePosition();
        }

        private void OnConfirmLayout()
        {
            if (!IsLayoutComplete)
            {
                StatusMessage = "Cannot confirm - layout incomplete";
                return;
            }

            LayoutConfirmed?.Invoke(this, EventArgs.Empty);
        }

        private void OnCancel()
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
        }

        public void Cleanup()
        {
            _coordinator.LayoutChanged -= OnLayoutChanged;
        }
    }
}
