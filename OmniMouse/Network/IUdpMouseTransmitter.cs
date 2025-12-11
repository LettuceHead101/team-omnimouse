using System;

namespace OmniMouse.Network
{
    // IUdpMouseTransmitter: contract for sending/receiving mouse positions over UDP.
    public interface IUdpMouseTransmitter
    {
        // Events
        event Action<ConnectionRole>? RoleChanged;
        event Action<int, int>? TakeControlReceived;
        event Action<FileShare.FileOfferPacket>? FileOfferReceived;
        // Start listening on the well-known port.
        void StartHost();

        // Start listening and proactively set the peer (active-sender host).
        void StartHost(string peerIp);

        // Configure sender to transmit to a remote host IP (legacy "sender" role).
        void StartCoHost(string hostIp);

        // Symmetric peer mode � bind to the well-known port and set the remote peer to send to.
        void StartPeer(string peerIp);

        // Set/replace the remote endpoint at runtime; triggers (re)handshake if possible.
        void SetRemotePeer(string hostOrIp);

        // Legacy: send raw screen pixel coordinates (int x, int y).
        void SendMousePosition(int x, int y);

        // When isDelta=true, encodes delta by adding/subtracting 100000 sentinel.
        void SendMouse(int x, int y, bool isDelta = false);

        // Send mouse button state (down/up)
        void SendMouseButton(MouseButtonNet button, bool isDown);

        // Send mouse wheel delta (positive or negative, 120 per notch typical)
        void SendMouseWheel(int delta);

        //// Send normalized coordinates in [0..1] (bottom-left = 0,0; top-right = 1,1).
        //void SendNormalizedMousePosition(float normalizedX, float normalizedY);

        // Cleanup / teardown the transmitter and its socket resources.
        void Disconnect();

        // Request the target client to take control at the specified local coordinates.
        void SendTakeControl(string targetClientId, int localX, int localY);

        // Request the target client to take control, including entry direction for return edge detection.
        void SendTakeControl(string targetClientId, int localX, int localY, OmniMouse.Switching.Direction? entryDirection);

        // Send layout position update to peers.
        void SendLayoutUpdate(int position, string machineId, string displayName);

        // Send grid layout position update to peers.
        void SendGridLayoutUpdate(string machineId, string displayName, int gridX, int gridY);

        // Send a file offer notification to the remote peer.
        void SendFileOffer(FileShare.FileOfferPacket offer);

        // Get the layout coordinator instance
        LayoutCoordinator? GetLayoutCoordinator();

        // Get the local machine ID
        string GetLocalMachineId();
    }
}