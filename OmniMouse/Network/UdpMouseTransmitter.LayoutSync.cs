using System;
using System.Linq;
using System.Text;
using System.Net;


namespace OmniMouse.Network
{
    public partial class UdpMouseTransmitter
    {
        private LayoutCoordinator? _layoutCoordinator;
        private IPEndPoint? _peerEndPoint;

        /// <summary>
        /// Gets the layout coordinator for this session.
        /// </summary>
        public LayoutCoordinator? LayoutCoordinator => _layoutCoordinator;

        /// <summary>
        /// Initializes layout coordination for this session.
        /// Should be called after handshake completes.
        /// </summary>
        /// <param name="localMachineId">The local machine identifier.</param>
        /// <param name="remoteMachineId">The remote machine identifier.</param>
        internal void InitializeLayoutCoordinator(string localMachineId, string remoteMachineId)
        {
            if (_layoutCoordinator != null)
            {
                Console.WriteLine("[UdpMouse][Layout] Coordinator already initialized");
                return;
            }

            _layoutCoordinator = new LayoutCoordinator(this, localMachineId);
            _layoutCoordinator.LayoutChanged += OnLayoutChangedHandler;

            // Announce the peer connection - use machine name as display name
            var remoteDisplayName = remoteMachineId.Split(':')[0]; // Extract IP from endpoint
            _layoutCoordinator.AnnouncePeerConnected(remoteMachineId, remoteDisplayName);

            Console.WriteLine($"[UdpMouse][Layout] Coordinator initialized for machine: {localMachineId}, peer: {remoteMachineId}");
        }

        private void OnLayoutChangedHandler(object? sender, LayoutChangedEventArgs e)
        {
            // TODO: Update MultiMachineSwitcher with new ordered machine IDs
            Console.WriteLine($"[UdpMouse][Layout] Layout changed: {e.Reason}");
        }

        /// <summary>
        /// Sends MSG_LAYOUT_UPDATE when local machine changes position.
        /// Format: [MSG_LAYOUT_UPDATE][position:int32][machineIdLength:int32][machineId:UTF8][displayNameLength:int32][displayName:UTF8]
        /// </summary>
        public void SendLayoutUpdate(int position, string machineId, string displayName)
        {
            if (_udpClient == null || _peerEndPoint == null)
            {
                Console.WriteLine("[UdpMouse][Layout] Cannot send layout update - not connected");
                return;
            }

            try
            {
                var machineIdBytes = Encoding.UTF8.GetBytes(machineId);
                var displayNameBytes = Encoding.UTF8.GetBytes(displayName);

                var packet = new byte[1 + 4 + 4 + machineIdBytes.Length + 4 + displayNameBytes.Length];
                int offset = 0;

                packet[offset++] = MSG_LAYOUT_UPDATE;

                BitConverter.GetBytes(position).CopyTo(packet, offset);
                offset += 4;

                BitConverter.GetBytes(machineIdBytes.Length).CopyTo(packet, offset);
                offset += 4;

                machineIdBytes.CopyTo(packet, offset);
                offset += machineIdBytes.Length;

                BitConverter.GetBytes(displayNameBytes.Length).CopyTo(packet, offset);
                offset += 4;

                displayNameBytes.CopyTo(packet, offset);

                _udpClient.Send(packet, packet.Length, _peerEndPoint);

                Console.WriteLine($"[UdpMouse][Layout] Sent layout update: {displayName} at position {position}");

                // Log consolidated layout after local update
                this.DumpLayoutSummary("[UdpMouse][Layout] Layout after local position update");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UdpMouse][Layout] Error sending layout update: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles incoming MSG_LAYOUT_UPDATE messages.
        /// </summary>
        private void HandleLayoutUpdate(byte[] packet, int offset)
        {
            if (_layoutCoordinator == null)
            {
                Console.WriteLine("[UdpMouse][Layout] Received layout update but coordinator not initialized");
                return;
            }

            try
            {
                // Parse: [position:int32][machineIdLength:int32][machineId:UTF8][displayNameLength:int32][displayName:UTF8]
                int position = BitConverter.ToInt32(packet, offset);
                offset += 4;

                int machineIdLength = BitConverter.ToInt32(packet, offset);
                offset += 4;

                string machineId = Encoding.UTF8.GetString(packet, offset, machineIdLength);
                offset += machineIdLength;

                int displayNameLength = BitConverter.ToInt32(packet, offset);
                offset += 4;

                string displayName = Encoding.UTF8.GetString(packet, offset, displayNameLength);

                Console.WriteLine($"[UdpMouse][Layout] Received layout update: {displayName} at position {position}");

                _layoutCoordinator.ApplyRemoteMachineUpdate(machineId, position, displayName);

                // Log consolidated layout after remote update
                this.DumpLayoutSummary("[UdpMouse][Layout] Layout after remote position update");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UdpMouse][Layout] Error handling layout update: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when layout changes (local or remote).
        /// </summary>
        private void OnLayoutChanged(object? sender, LayoutChangedEventArgs e)
        {
            Console.WriteLine($"[UdpMouse][Layout] Layout changed: {e.Reason}");

            // If layout is complete, update the MultiMachineSwitcher
            if (e.Layout.OrderedMachines.Any())
            {
                var orderedIds = e.Layout.GetOrderedMachineIds();
                Console.WriteLine($"[UdpMouse][Layout] Ordered machines: {string.Join(", ", orderedIds)}");

                // TODO: Update MultiMachineSwitcher.UpdateMatrix with orderedIds
                // This will be wired in HomePageViewModel
            }
        }

        /// <summary>
        /// Announces a newly connected peer to the layout coordinator.
        /// </summary>
        internal void AnnouncePeerToLayout(string peerId, string displayName)
        {
            _layoutCoordinator?.AnnouncePeerConnected(peerId, displayName);
        }

        /// <summary>
        /// Gets the layout coordinator instance.
        /// </summary>
        public LayoutCoordinator? GetLayoutCoordinator()
        {
            return _layoutCoordinator;
        }

        /// <summary>
        /// Gets the local machine ID generated from the UDP endpoint.
        /// </summary>
        public string GetLocalMachineId()
        {
            var localEp = _udpClient?.Client.LocalEndPoint as IPEndPoint;
            return $"{localEp?.Address}:{localEp?.Port}";
        }
    }
}
