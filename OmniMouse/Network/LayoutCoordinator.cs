using System;
using System.Linq;

namespace OmniMouse.Network
{
    /// <summary>
    /// Coordinates layout synchronization across all connected machines.
    /// Manages position negotiation and broadcasts layout changes.
    /// </summary>
    public class LayoutCoordinator
    {
        private readonly IUdpMouseTransmitter _transmitter;
        private readonly string _localMachineId;
        private SessionLayout _currentLayout;

        public event EventHandler<LayoutChangedEventArgs>? LayoutChanged;

        public SessionLayout CurrentLayout => _currentLayout.Clone();

        public LayoutCoordinator(IUdpMouseTransmitter transmitter, string localMachineId)
        {
            _transmitter = transmitter ?? throw new ArgumentNullException(nameof(transmitter));
            _localMachineId = localMachineId ?? throw new ArgumentNullException(nameof(localMachineId));
            
            _currentLayout = new SessionLayout
            {
                AuthorityMachineId = localMachineId
            };

            // Add local machine as unpositioned initially
            _currentLayout.Machines.Add(new ConnectedMachine
            {
                MachineId = localMachineId,
                DisplayName = Environment.MachineName,
                Position = -1,  // Unassigned - user must choose
                IsLocal = true
            });
        }

        /// <summary>
        /// Announces a newly connected peer machine (called when handshake completes).
        /// </summary>
        public void AnnouncePeerConnected(string peerId, string displayName)
        {
            Console.WriteLine($"[LayoutCoordinator] Peer connected: {peerId} ({displayName})");

            var existing = _currentLayout.Machines.FirstOrDefault(m => m.MachineId == peerId);
            if (existing == null)
            {
                _currentLayout.Machines.Add(new ConnectedMachine
                {
                    MachineId = peerId,
                    DisplayName = displayName,
                    Position = -1,  // Unassigned
                    IsLocal = false
                });

                OnLayoutChanged($"Peer {displayName} joined session");
            }
        }

        /// <summary>
        /// User sets their local machine's position (via drag-drop UI).
        /// Broadcasts the update to all peers.
        /// </summary>
        public void SetLocalPosition(int position)
        {
            var localMachine = _currentLayout.Machines.FirstOrDefault(m => m.IsLocal);
            if (localMachine != null)
            {
                SetMachinePosition(localMachine.MachineId, position);
            }
        }

        /// <summary>
        /// Sets any machine's position (local or peer) and broadcasts the update.
        /// Allows any PC to arrange the entire layout.
        /// </summary>
        public void SetMachinePosition(string machineId, int position)
        {
            Console.WriteLine($"[LayoutCoordinator] Setting position for {machineId} to {position}");

            var machine = _currentLayout.Machines.FirstOrDefault(m => m.MachineId == machineId);
            if (machine == null)
            {
                Console.WriteLine($"[LayoutCoordinator] ERROR: Machine {machineId} not found in layout!");
                return;
            }

            // Check if position is already taken by another machine
            var conflict = _currentLayout.Machines.FirstOrDefault(m => m.Position == position && m.MachineId != machineId);
            if (conflict != null)
            {
                Console.WriteLine($"[LayoutCoordinator] WARNING: Position {position} occupied by {conflict.DisplayName}");
                // For now, allow - UI should prevent this
            }

            machine.Position = position;
            _currentLayout.LastUpdateTimestamp = DateTime.UtcNow;

            // Broadcast the specific machine's position update to all peers
            BroadcastMachineUpdate(machine);

            OnLayoutChanged($"{machine.DisplayName} position set to {position}");
        }

        /// <summary>
        /// Broadcasts a specific machine's position update to all connected peers.
        /// </summary>
        private void BroadcastMachineUpdate(ConnectedMachine machine)
        {
            if (!machine.IsPositioned)
            {
                Console.WriteLine($"[LayoutCoordinator] Skipping broadcast - machine {machine.DisplayName} not positioned");
                return;
            }

            // Send layout update via UDP transmitter (works for both local and peer machines)
            _transmitter.SendLayoutUpdate(machine.Position, machine.MachineId, machine.DisplayName);
            Console.WriteLine($"[LayoutCoordinator] Broadcast position update - {machine.DisplayName} at position {machine.Position}");
        }

        /// <summary>
        /// Applies a layout update received from a peer.
        /// </summary>
        public void ApplyRemoteLayoutUpdate(SessionLayout remoteLayout)
        {
            Console.WriteLine($"[LayoutCoordinator] Applying remote layout update from {remoteLayout.AuthorityMachineId}");

            // Merge remote machines into our layout (preserve local machine state)
            foreach (var remoteMachine in remoteLayout.Machines)
            {
                if (remoteMachine.MachineId == _localMachineId)
                    continue; // Don't overwrite local machine

                var existing = _currentLayout.Machines.FirstOrDefault(m => m.MachineId == remoteMachine.MachineId);
                if (existing != null)
                {
                    existing.Position = remoteMachine.Position;
                    existing.DisplayName = remoteMachine.DisplayName;
                }
                else
                {
                    _currentLayout.Machines.Add(remoteMachine.Clone());
                }
            }

            _currentLayout.LastUpdateTimestamp = DateTime.UtcNow;

            OnLayoutChanged("Remote layout update applied");
        }

        /// <summary>
        /// Applies a single machine position update from a peer.
        /// </summary>
        public void ApplyRemoteMachineUpdate(string machineId, int position, string displayName)
        {
            Console.WriteLine($"[LayoutCoordinator] Remote update: {machineId} -> position {position}");
            var machine = _currentLayout.Machines.FirstOrDefault(m => m.MachineId == machineId);
            if (machine != null)
            {
                machine.Position = position;
                machine.DisplayName = displayName;
                if (machineId == _localMachineId)
                {
                    Console.WriteLine("[LayoutCoordinator] Applied remote update to local machine (peer authority)");
                }
            }
            else
            {
                _currentLayout.Machines.Add(new ConnectedMachine
                {
                    MachineId = machineId,
                    DisplayName = displayName,
                    Position = position,
                    IsLocal = false
                });
            }

            _currentLayout.LastUpdateTimestamp = DateTime.UtcNow;

            OnLayoutChanged($"{displayName} moved to position {position}");
        }

        /// <summary>
        /// Removes a disconnected peer from the layout.
        /// </summary>
        public void RemovePeer(string peerId)
        {
            Console.WriteLine($"[LayoutCoordinator] Removing peer: {peerId}");

            var machine = _currentLayout.Machines.FirstOrDefault(m => m.MachineId == peerId);
            if (machine != null)
            {
                _currentLayout.RemoveMachine(peerId);
                OnLayoutChanged($"{machine.DisplayName} disconnected");
            }
        }

        /// <summary>
        /// Checks if the layout is ready (all machines positioned).
        /// </summary>
        public bool IsLayoutComplete()
        {
            return _currentLayout.Machines.All(m => m.IsPositioned);
        }

        /// <summary>
        /// Broadcasts the current layout to all connected peers.
        /// </summary>
        private void BroadcastLayoutUpdate()
        {
            var localMachine = _currentLayout.Machines.FirstOrDefault(m => m.IsLocal);
            if (localMachine != null && localMachine.IsPositioned)
            {
                BroadcastMachineUpdate(localMachine);
            }
            else
            {
                Console.WriteLine($"[LayoutCoordinator] Skipping broadcast - local machine not positioned");
            }
        }

        private void OnLayoutChanged(string reason)
        {
            Console.WriteLine($"[LayoutCoordinator] Layout changed: {reason}");
            Console.WriteLine($"  Current layout: {string.Join(", ", _currentLayout.OrderedMachines.Select(m => $"{m.DisplayName}[{m.Position}]"))}");

            LayoutChanged?.Invoke(this, new LayoutChangedEventArgs(_currentLayout.Clone(), reason));
        }
    }
}
