namespace OmniMouse.Network
{
    /// <summary>
    /// Contract for sending/receiving keyboard input over UDP.
    /// Follows the same pattern as mouse input transmission.
    /// </summary>
    public interface IUdpKeyboardTransmitter
    {
        /// <summary>
        /// Send keyboard key state (down/up) to remote peer.
        /// </summary>
        /// <param name="vkCode">Virtual key code</param>
        /// <param name="scanCode">Hardware scan code</param>
        /// <param name="isDown">True for key down, false for key up</param>
        /// <param name="flags">Additional keyboard flags from low-level hook</param>
        void SendKeyboard(int vkCode, int scanCode, bool isDown, int flags);
    }
}
