using System;
using System.Runtime.InteropServices;
using OmniMouse.Network;

namespace OmniMouse.Hooks
{
    public partial class InputHooks
    {
        private static IntPtr KbHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // Bypass our own synthetic keyboard input (same pattern as mouse)
            if (_isSyntheticKeyboard)
            {
                _isSyntheticKeyboard = false; // Reset flag
                //Console.WriteLine("[HOOK][Keyboard] Bypassing synthetic input");
                if (_instance != null)
                    return CallNextHookExImpl(_instance._kbHook, nCode, wParam, lParam);
                return IntPtr.Zero;
            }

            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                // ignore injected keyboard input for control shortcuts recognition.
                if (!IsInjectedKey(kb.flags))
                {
                    var key = (ConsoleKey)kb.vkCode;
                    Console.WriteLine($"[KEY] {key}");
                    
                    // Control shortcut: Ctrl+Shift+Q to quit
                    if (Ctrl && Shift && key == ConsoleKey.Q)
                    {
                        PostQuitMessage(0);
                    }

                    // Handle keyboard forwarding when in remote streaming mode
                    // Same logic as mouse: if we're streaming to remote, send keyboard there
                    if (_remoteStreaming && _instance?._udpTransmitter is IUdpKeyboardTransmitter kbTransmitter)
                    {
                        Console.WriteLine($"[KEY][Stream] Forwarding {key} to remote (vk={kb.vkCode}, scan={kb.scanCode})");
                        kbTransmitter.SendKeyboard(kb.vkCode, kb.scanCode, isDown: true, kb.flags);
                        
                        // Block local input when streaming to remote
                        // Return 1 to prevent the key from being processed locally
                        return (IntPtr)1;
                    }
                }
            }
            else if (nCode >= 0 && (wParam == (IntPtr)0x0101 || wParam == (IntPtr)0x0105)) // WM_KEYUP and WM_SYSKEYUP
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                // Handle key up events when streaming
                if (!IsInjectedKey(kb.flags) && _remoteStreaming && _instance?._udpTransmitter is IUdpKeyboardTransmitter kbTransmitter)
                {
                    var key = (ConsoleKey)kb.vkCode;
                    Console.WriteLine($"[KEY][Stream] Forwarding {key} UP to remote (vk={kb.vkCode}, scan={kb.scanCode})");
                    kbTransmitter.SendKeyboard(kb.vkCode, kb.scanCode, isDown: false, kb.flags);
                    
                    // Block local input when streaming to remote
                    return (IntPtr)1;
                }
            }
            
            if (_instance != null)
                return CallNextHookEx(_instance._kbHook, nCode, wParam, lParam);
            return IntPtr.Zero;
        }
    }
}