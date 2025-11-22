namespace OmniMouse.Network
{
    /// <summary>
    /// Network-friendly representation of mouse buttons.
    /// Values chosen to match existing wire-format expectations (1..3).
    /// </summary>
    public enum MouseButtonNet : byte
    {
        Left = 1,
        Right = 2,
        Middle = 3
    }
}
