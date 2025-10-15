using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OmniMouse.Network;

namespace OmniMouse.Hooks
{
    public interface IInputHooks
    {
        void InstallHooks();
        void UninstallHooks();
        void RunMessagePump();
    }

    public class InputHooks : IInputHooks
    {
        private readonly IUdpMouseTransmitter _udpTransmitter;
        private IntPtr _kbHook = IntPtr.Zero;
        private IntPtr _mouseHook = IntPtr.Zero;
        private static readonly HookProc _kbProc;
        private static readonly HookProc _mouseProc;
        private static InputHooks? _instance;

        // Win32 constants
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MOUSEWHEEL = 0x020A;

        // Win32 structs
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public int mouseData;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
            public uint lPrivate;
        }

        // Win32 delegates
        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        // P/Invoke declarations
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")]
        private static extern bool TranslateMessage([In] ref MSG lpMsg);
        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);
        [DllImport("user32.dll")]
        private static extern bool PostQuitMessage(int nExitCode);
        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        static InputHooks()
        {
            _kbProc = KbHookCallback;
            _mouseProc = MouseHookCallback;
        }

        public InputHooks(IUdpMouseTransmitter udpTransmitter)
        {
            _udpTransmitter = udpTransmitter;
            _instance = this;
        }

        public void InstallHooks()
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            IntPtr hMod = GetModuleHandle(curModule.ModuleName);
            _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, hMod, 0);
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
            if (_kbHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
            {
                Console.Error.WriteLine("Failed to install hooks. Try running as Administrator.");
                UninstallHooks();
                throw new Exception("Failed to install hooks");
            }
        }

        public void UninstallHooks()
        {
            if (_kbHook != IntPtr.Zero) { UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
            if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        }

        public void RunMessagePump()
        {
            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        private static bool Ctrl => (GetKeyState(0x11) & 0x8000) != 0; // VK_CONTROL
        private static bool Shift => (GetKeyState(0x10) & 0x8000) != 0; // VK_SHIFT

        private static IntPtr KbHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var key = (ConsoleKey)kb.vkCode;
                Console.WriteLine($"[KEY] {key}");
                if (Ctrl && Shift && key == ConsoleKey.Q)
                {
                    PostQuitMessage(0);
                }
            }
            if (_instance != null)
                return CallNextHookEx(_instance._kbHook, nCode, wParam, lParam);
            return IntPtr.Zero;
        }

        private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                switch ((int)wParam)
                {
                    case WM_MOUSEMOVE:
                        // Existing behavior logged raw mouse coords for debugging.
                        Console.WriteLine($"[MOVE] ({ms.pt.x}, {ms.pt.y})");

                        if (_instance != null)
                        {
                            try
                            {
                                // IMPORTANT CHANGE:
                                // - We no longer send raw pixel ints here.
                                // - Instead we normalize the raw screen coordinates (ms.pt) to [0..1]
                                //   relative to the sender's virtual desktop, then send those floats.
                                // - The receiver maps those normalized values to its own virtual desktop.
                                //
                                // This change is the main fix for resolution/aspect-ratio parity issues.
                                CoordinateNormalizer.ScreenToNormalized(ms.pt.x, ms.pt.y, out var nx, out var ny);
                                _instance._udpTransmitter?.SendNormalizedMousePosition(nx, ny);
                            }
                            catch (Exception ex)
                            {
                                // Hooks must be robust; swallow exceptions and log so hooks don't crash the process.
                                Console.WriteLine($"[HOOK] Failed to send normalized mouse position: {ex.Message}");
                            }
                        }
                        break;
                    case WM_LBUTTONDOWN:
                        Console.WriteLine($"[LBTN] ({ms.pt.x}, {ms.pt.y})");
                        break;
                    case WM_RBUTTONDOWN:
                        Console.WriteLine($"[RBTN] ({ms.pt.x}, {ms.pt.y})");
                        break;
                    case WM_MBUTTONDOWN:
                        Console.WriteLine($"[MBTN] ({ms.pt.x}, {ms.pt.y})");
                        break;
                    case WM_MOUSEWHEEL:
                        int delta = (short)((ms.mouseData >> 16) & 0xffff);
                        Console.WriteLine($"[WHEEL] delta={delta} at ({ms.pt.x}, {ms.pt.y})");
                        break;
                }
            }
            if (_instance != null)
                return CallNextHookEx(_instance._mouseHook, nCode, wParam, lParam);
            return IntPtr.Zero;
        }
    }
}