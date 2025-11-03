using System;

namespace OmniMouse.Model
{
    public sealed class PeerInfo
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
        public string Ip { get; init; } = "";
        public int Port { get; init; } = 5000; // data port (UdpMouseTransmitter)
        public DateTime LastSeenUtc { get; set; }
    }
}