using MessagePack;

namespace OmniMouse.Core.Packets
{
    [MessagePackObject(keyAsPropertyName: true)]
    public class MousePacket
    {
        [Key(0)] public int X { get; set; }
        [Key(1)] public int Y { get; set; }
    }
}
