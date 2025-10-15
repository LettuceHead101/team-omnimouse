using System.Net;

namespace OmniMouse.Network
{
    // IUdpMouseTransmitter: contract for sending/receiving mouse positions over UDP.
    // Changes: interface extended to include normalized send and Disconnect, keeping
    //         backward-compatible SendMousePosition (raw ints) for older clients.
    public interface IUdpMouseTransmitter
    {
        // Start listening for incoming mouse packets (Cohost/receiver).
        void StartHost();

        // Configure sender to transmit to a remote host IP (Host/cohost relationship reversed naming).
        void StartCoHost(string hostIp);

        // Legacy: send raw screen pixel coordinates (int x, int y).
        // Kept for backward compatibility with older clients.
        void SendMousePosition(int x, int y);

        // NEW: send normalized coordinates in [0..1] (bottom-left = 0,0; top-right = 1,1).
        // Use this to avoid resolution/aspect-ratio mismatches between machines.
        void SendNormalizedMousePosition(float normalizedX, float normalizedY);

        // Cleanup / teardown the transmitter and its socket resources.
        void Disconnect();
    }
}