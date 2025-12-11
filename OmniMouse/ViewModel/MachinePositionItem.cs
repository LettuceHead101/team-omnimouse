using OmniMouse.MVVM;

namespace OmniMouse.ViewModel
{
    /// <summary>
    /// Represents a machine in the layout UI.
    /// </summary>
    public class MachinePositionItem : ViewModelBase
    {
        private string _machineId = string.Empty;
        private string _displayName = string.Empty;
        private int _position = -1;
        private int _gridX = -1;
        private int _gridY = -1;
        private bool _isLocal;
        private bool _isPositioned;
        private bool _isEmptySlot;

        public string MachineId
        {
            get => _machineId;
            set => SetProperty(ref _machineId, value);
        }

        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        public int Position
        {
            get => _position;
            set => SetProperty(ref _position, value);
        }

        public int GridX
        {
            get => _gridX;
            set
            {
                if (SetProperty(ref _gridX, value))
                {
                    OnPropertyChanged(nameof(GridPositionDisplay));
                }
            }
        }

        public int GridY
        {
            get => _gridY;
            set
            {
                if (SetProperty(ref _gridY, value))
                {
                    OnPropertyChanged(nameof(GridPositionDisplay));
                }
            }
        }

        public bool IsLocal
        {
            get => _isLocal;
            set => SetProperty(ref _isLocal, value);
        }

        public bool IsPositioned
        {
            get => _isPositioned;
            set => SetProperty(ref _isPositioned, value);
        }

        public bool IsEmptySlot
        {
            get => _isEmptySlot;
            set => SetProperty(ref _isEmptySlot, value);
        }

        public string PositionDisplay => IsPositioned ? $"Position {Position}" : "Unassigned";
        public string GridPositionDisplay => (GridX >= 0 && GridY >= 0) ? $"[{GridX}, {GridY}]" : "Unassigned";
        public string TypeDisplay => IsLocal ? "YOU" : "PEER";
    }
}
