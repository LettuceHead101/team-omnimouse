using System.Net;

namespace OmniMouse.Network
{
    public interface IUdpMouseTransmitter
    {
        void StartHost();
        void StartCoHost(string hostIp);
        void SendMousePosition(int x, int y);
        void SendNormalizedMousePosition(float normalizedX, float normalizedY); // NEW
        void Disconnect();
    }
}