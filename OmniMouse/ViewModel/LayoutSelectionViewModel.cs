using OmniMouse.MVVM;
using OmniMouse.Network;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace OmniMouse.ViewModel
{
    /// <summary>
    /// ViewModel for the layout selection dialog.
    /// Manages the visual representation of connected machines and their positions.
    /// </summary>
    public class LayoutSelectionViewModel : ViewModelBase
    {
        private readonly LayoutCoordinator _coordinator;
        private readonly string _localMachineId;
        private ObservableCollection<MachinePositionItem> _machines = new();
        private bool _isLayoutComplete;
        private string _statusMessage = "Arrange your PC position using drag & drop";

        public ObservableCollection<MachinePositionItem> Machines
        {
            get => _machines;
            set => SetProperty(ref _machines, value);
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

            // First, add all unpositioned machines so users can see what needs to be dragged
            var unpositionedMachines = layout.Machines.Where(m => !m.IsPositioned).ToList();
            foreach (var machine in unpositionedMachines)
            {
                Machines.Add(new MachinePositionItem
                {
                    MachineId = machine.MachineId,
                    DisplayName = machine.DisplayName,
                    Position = -1, // No position yet
                    IsLocal = machine.IsLocal,
                    IsPositioned = false,
                    IsEmptySlot = false,
                });
            }

            // Determine how many position slots we need to show
            int totalMachines = layout.Machines.Count;
            int maxPosition = layout.Machines.Where(m => m.IsPositioned).Any() 
                ? layout.Machines.Where(m => m.IsPositioned).Max(m => m.Position) 
                : -1;
            int numSlots = Math.Max(totalMachines, maxPosition + 1);

            // Create fixed position slots (0, 1, 2, ...) as drop targets
            for (int pos = 0; pos < numSlots; pos++)
            {
                var machineAtPos = layout.Machines.FirstOrDefault(m => m.IsPositioned && m.Position == pos);
                
                if (machineAtPos != null)
                {
                    // This slot has a machine - show the machine
                    Machines.Add(new MachinePositionItem
                    {
                        MachineId = machineAtPos.MachineId,
                        DisplayName = machineAtPos.DisplayName,
                        Position = pos,
                        IsLocal = machineAtPos.IsLocal,
                        IsPositioned = true,
                        IsEmptySlot = false,
                    });
                }
                else
                {
                    // Empty slot - show as drop target with minimal text
                    Machines.Add(new MachinePositionItem
                    {
                        MachineId = $"empty_{pos}",
                        DisplayName = "Unassigned",
                        Position = pos,
                        IsLocal = false,
                        IsPositioned = false,
                        IsEmptySlot = true,
                    });
                }
            }

            UpdateStatus();
        }

        private void OnLayoutChanged(object? sender, LayoutChangedEventArgs e)
        {
            RefreshMachines();
        }

        private void UpdateStatus()
        {
            // Only count actual machines that are unpositioned (exclude any placeholder slots)
            var unpositioned = Machines.Where(m => !m.IsPositioned && !m.MachineId.StartsWith("empty_")).ToList();
            IsLayoutComplete = unpositioned.Count == 0;

            if (IsLayoutComplete)
            {
                StatusMessage = "Layout complete! Click Confirm to continue.";
            }
            else
            {
                StatusMessage = $"Drag any PC card to position ({unpositioned.Count} unassigned)";
            }
        }

        /// <summary>
        /// Called when user drags any machine to a new position.
        /// </summary>
        public void MoveMachine(string machineId, int newPosition)
        {
            var machine = Machines.FirstOrDefault(m => m.MachineId == machineId);
            if (machine == null || machine.MachineId.StartsWith("empty_"))
            {
                return;
            }

            // Don't allow dropping on occupied positions (only check real machines, not empty slots)
            var conflict = Machines.FirstOrDefault(m => m.Position == newPosition && m.MachineId != machineId && m.IsPositioned && !m.MachineId.StartsWith("empty_"));
            if (conflict != null)
            {
                StatusMessage = $"Position {newPosition} is occupied by {conflict.DisplayName}";
                return;
            }

            // Update any machine's position (works for both local and peer machines)
            _coordinator.SetMachinePosition(machineId, newPosition);

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
