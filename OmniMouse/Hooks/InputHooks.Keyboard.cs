using System;
using System.Runtime.InteropServices;

namespace OmniMouse.Hooks
{
    public partial class InputHooks
    {
        private static IntPtr KbHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                // ignore injected keyboard input for control shortcuts recognition.
                if (!IsInjectedKey(kb.flags))
                {
                    var key = (ConsoleKey)kb.vkCode;
                    Console.WriteLine($"[KEY] {key}");
                    if (Ctrl && Shift && key == ConsoleKey.Q)
                    {
                        PostQuitMessage(0);
                    }
                }
            }
            if (_instance != null)
                return CallNextHookEx(_instance._kbHook, nCode, wParam, lParam);
            return IntPtr.Zero;
        }
    }
}