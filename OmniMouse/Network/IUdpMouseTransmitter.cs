using System.Net;

namespace OmniMouse.Network
{
    // IUdpMouseTransmitter: contract for sending/receiving mouse positions over UDP.
    public interface IUdpMouseTransmitter
    {
        // Start listening for incoming mouse packets (legacy "receiver" role).
        void StartHost();

        // Configure sender to transmit to a remote host IP (legacy "sender" role).
        void StartCoHost(string hostIp);

        // NEW: Symmetric peer mode — bind to the well-known port and set the remote peer to send to.
        void StartPeer(string peerIp);

        // Send normalized coordinates in [0..1] (bottom-left = 0,0; top-right = 1,1).
        void SendNormalizedMousePosition(float normalizedX, float normalizedY);

        // Cleanup / teardown the transmitter and its socket resources.
        void Disconnect();
    }
}