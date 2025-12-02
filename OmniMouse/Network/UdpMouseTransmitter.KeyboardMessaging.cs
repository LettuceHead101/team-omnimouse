using System;
using System.Net;
using OmniMouse.Hooks;

namespace OmniMouse.Network
{
    /// <summary>
    /// Keyboard messaging extension for UdpMouseTransmitter.
    /// Handles keyboard input transmission and reception following the same pattern as mouse input.
    /// </summary>
    public partial class UdpMouseTransmitter : IUdpKeyboardTransmitter
    {
        // Message type constants for keyboard (keeping separate from mouse constants)
        private const byte MSG_KEYBOARD_DOWN = 0x09;
        private const byte MSG_KEYBOARD_UP = 0x0A;

        /// <summary>
        /// Sends a keyboard key event (down/up) to the remote peer.
        /// Only sends if handshake is complete and we are in Sender role.
        /// </summary>
        /// <param name="vkCode">Virtual key code</param>
        /// <param name="scanCode">Hardware scan code</param>
        /// <param name="isDown">True for key down, false for key up</param>
        /// <param name="flags">Additional keyboard flags from low-level hook</param>
        public void SendKeyboard(int vkCode, int scanCode, bool isDown, int flags)
        {
            // Same role-gating as mouse input
            lock (_roleLock)
            {
                if (!_handshakeComplete || _currentRole != ConnectionRole.Sender)
                    return;
            }
            if (_udpClient == null || _remoteEndPoint == null) return;

            // Packet format: [MSG_TYPE(1)] [vkCode(4)] [scanCode(4)] [flags(4)]
            var buf = new byte[1 + 4 + 4 + 4];
            buf[0] = isDown ? MSG_KEYBOARD_DOWN : MSG_KEYBOARD_UP;
            Array.Copy(BitConverter.GetBytes(vkCode), 0, buf, 1, 4);
            Array.Copy(BitConverter.GetBytes(scanCode), 0, buf, 1 + 4, 4);
            Array.Copy(BitConverter.GetBytes(flags), 0, buf, 1 + 8, 4);
            
            _udpClient.Send(buf, buf.Length, _remoteEndPoint);
            Console.WriteLine($"[UDP][SendKey] vk={vkCode} scan={scanCode} {(isDown ? "DOWN" : "UP")} flags=0x{flags:X}");
        }

        /// <summary>
        /// Handles incoming keyboard down event from remote peer.
        /// Called from the receive loop when MSG_KEYBOARD_DOWN is received.
        /// </summary>
        private void HandleKeyboardDown(byte[] data, IPEndPoint remoteEP)
        {
            // Role check: only inject if we're Receiver
            lock (_roleLock)
            {
                if (!_handshakeComplete || _currentRole != ConnectionRole.Receiver)
                    return;
            }

            // Source validation: only accept from known peer
            if (_remoteEndPoint != null && !remoteEP.Address.Equals(_remoteEndPoint.Address))
                return;

            if (data.Length >= 1 + 4 + 4 + 4)
            {
                int vkCode = BitConverter.ToInt32(data, 1);
                int scanCode = BitConverter.ToInt32(data, 1 + 4);
                int flags = BitConverter.ToInt32(data, 1 + 8);
                
                Console.WriteLine($"[UDP][RecvKey] vk={vkCode} scan={scanCode} DOWN flags=0x{flags:X}");
                InputHooks.InjectKeyboard(vkCode, scanCode, isDown: true);
            }
        }

        /// <summary>
        /// Handles incoming keyboard up event from remote peer.
        /// Called from the receive loop when MSG_KEYBOARD_UP is received.
        /// </summary>
        private void HandleKeyboardUp(byte[] data, IPEndPoint remoteEP)
        {
            // Role check: only inject if we're Receiver
            lock (_roleLock)
            {
                if (!_handshakeComplete || _currentRole != ConnectionRole.Receiver)
                    return;
            }

            // Source validation: only accept from known peer
            if (_remoteEndPoint != null && !remoteEP.Address.Equals(_remoteEndPoint.Address))
                return;

            if (data.Length >= 1 + 4 + 4 + 4)
            {
                int vkCode = BitConverter.ToInt32(data, 1);
                int scanCode = BitConverter.ToInt32(data, 1 + 4);
                int flags = BitConverter.ToInt32(data, 1 + 8);
                
                Console.WriteLine($"[UDP][RecvKey] vk={vkCode} scan={scanCode} UP flags=0x{flags:X}");
                InputHooks.InjectKeyboard(vkCode, scanCode, isDown: false);
            }
        }
    }
}
