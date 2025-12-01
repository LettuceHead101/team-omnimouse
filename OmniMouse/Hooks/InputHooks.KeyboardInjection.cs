using System;
using System.Runtime.InteropServices;

namespace OmniMouse.Hooks
{
    public partial class InputHooks
    {
        // Flag to bypass our own keyboard injections (same pattern as mouse)
        private static volatile bool _isSyntheticKeyboard = false;

        // Test seam: observer invoked with (vkCode, scanCode, isDown, dwFlags) before SendInput.
        // Internal for access by NetworkTestProject1 via InternalsVisibleTo.
        internal static Action<int, int, bool, int>? InjectKeyboardObserver;

        /// <summary>
        /// Injects a keyboard key press/release using SendInput.
        /// CRITICAL: Sets _isSyntheticKeyboard flag BEFORE calling SendInput so our hook bypasses it.
        /// </summary>
        /// <param name="vkCode">Virtual key code</param>
        /// <param name="scanCode">Hardware scan code</param>
        /// <param name="isDown">True for key down, false for key up</param>
        public static void InjectKeyboard(int vkCode, int scanCode, bool isDown)
        {
            // Set the flag BEFORE SendInput so the hook will bypass this event
            _isSyntheticKeyboard = true;

            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUT_UNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = (ushort)vkCode,
                        wScan = (ushort)scanCode,
                        dwFlags = isDown ? 0 : KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            // Invoke test observer prior to native SendInput for verification without P/Invoke dependence
            try { InjectKeyboardObserver?.Invoke(vkCode, scanCode, isDown, isDown ? 0 : KEYEVENTF_KEYUP); } catch { }

            uint result = SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));

            if (result == 0)
            {
                var err = Marshal.GetLastWin32Error();
                Console.WriteLine($"[HOOK][InjectKey] SendInput FAILED vk={vkCode} scan={scanCode} {(isDown ? "DOWN" : "UP")} err={err}");
            }
            else
            {
                Console.WriteLine($"[HOOK][InjectKey] SUCCESS vk={vkCode} scan={scanCode} {(isDown ? "DOWN" : "UP")}");
            }
        }


    }
}
