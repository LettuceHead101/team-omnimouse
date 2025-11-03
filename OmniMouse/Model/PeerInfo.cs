using System;

namespace OmniMouse.Model
{
    public sealed class PeerInfo
    {
        public Guid Id { get; init; }
        public string Name { get; set; } = "";
        public string Ip   { get; set; } = "";
        public int Port    { get; set; } = 5000; // data port (UdpMouseTransmitter)
        public DateTime LastSeenUtc { get; set; }
    }
}