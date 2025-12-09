using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OmniMouse.Network;
using OmniMouse.Switching;

namespace OmniMouse.Hooks
{
    public interface IInputHooks
    {
        void InstallHooks();

        void UninstallHooks();

        void RunMessagePump();
    }

    public partial class InputHooks : IInputHooks
    {
        private readonly IUdpMouseTransmitter _udpTransmitter;
        private readonly InputCoordinator _inputCoordinator;
        private readonly IMultiMachineSwitcher? _switcher;
        private IntPtr _kbHook = IntPtr.Zero;
        private IntPtr _mouseHook = IntPtr.Zero;
        private static readonly HookProc _kbProc;
        private static readonly HookProc _mouseProc;
        private static InputHooks? _instance;

        //track last seen mouse position so we can compute deltas for InputCoordinator.
        private int _lastMouseX;
        private int _lastMouseY;
        private int _lastActualCursorX;
        private int _lastActualCursorY;

        // Feedback-loop suppression
        private static int _suppressX = int.MinValue;
        private static int _suppressY = int.MinValue;
        private static int _suppressCount = 0;

        // One-time log to confirm hook entry
        private static bool _loggedFirstMouseCallback = false;
        private static bool _loggedMissingSwitcher = false;

        // Track the message pump thread so we can stop it on uninstall
        private static uint _messageThreadId = 0;

        // Gate the sending path so role transitions don't race with hook dispatch
        private static readonly object _sendGate = new object();

        // Protects suppression state shared across hook and UDP threads
        private static readonly object _suppressionLock = new object();

        // When true, the hook will allow the input through without processing/blocking
        private static volatile bool _isSyntheticInput = false;

        
        public event Action? LocalMouseActivity;

        // Indicates that this machine is currently controlling a remote cursor.
        // When true, we stream local WM_MOUSEMOVE events to the peer instead of
        // evaluating edge switching locally.
        private static volatile bool _remoteStreaming = false;
        private static OmniMouse.Switching.Direction? _remoteStreamingDirection = null;
        private static int _remoteStreamingReleaseAccum = 0; // accumulates opposite-direction movement attempts
        private const int RemoteReleaseThresholdPixels = 40; // movement attempts needed to release control
        
        // Track accumulated remote cursor position (summing deltas sent)
        private static int _remoteCursorX = 0;
        private static int _remoteCursorY = 0;

        // Edge detection config
        private const int EdgeThresholdPixels = 2;
        public static string? RemotePeerClientId { get; private set; }
        public static void SetRemotePeer(string clientId) => RemotePeerClientId = clientId;

        public static void BeginRemoteStreaming()
        {
            BeginRemoteStreaming(null);
        }

        public static void BeginRemoteStreaming(OmniMouse.Switching.Direction? direction)
        {
            _remoteStreaming = true;
            _remoteStreamingDirection = direction;
            _remoteStreamingReleaseAccum = 0;
            
            // Initialize remote cursor position in REMOTE coordinate space (not local!)
            // We need to map the entry point based on which edge we crossed
            if (GetCursorPosImpl(out var cur) && !string.IsNullOrEmpty(RemotePeerClientId))
            {
                // Get remote machine's monitor bounds
                var monitors = _instance?._inputCoordinator?.ScreenMap?.GetMonitorsSnapshot();
                if (monitors != null && monitors.Count > 0)
                {
                    // Find remote machine's total bounds
                    int remoteLeft = int.MaxValue, remoteTop = int.MaxValue;
                    int remoteRight = int.MinValue, remoteBottom = int.MinValue;

                    foreach (var monitor in monitors)
                    {
                        if (monitor.OwnerClientId == RemotePeerClientId)
                        {
                            // Use GlobalBounds to get virtual coordinate space bounds
                            var gb = monitor.GlobalBounds;
                            int gLeft = gb.X;
                            int gTop = gb.Y;
                            int gRight = gb.X + gb.Width;
                            int gBottom = gb.Y + gb.Height;
                            remoteLeft = Math.Min(remoteLeft, gLeft);
                            remoteTop = Math.Min(remoteTop, gTop);
                            remoteRight = Math.Max(remoteRight, gRight);
                            remoteBottom = Math.Max(remoteBottom, gBottom);
                        }
                    }

                    // Clamp entry Y/X to remote bounds
                    int entryY = Math.Max(remoteTop, Math.Min(remoteBottom - 1, cur.y));
                    int entryX = Math.Max(remoteLeft, Math.Min(remoteRight - 1, cur.x));

                    // Map entry point based on exit direction
                    if (remoteLeft != int.MaxValue && direction.HasValue)
                    {
                        switch (direction.Value)
                        {
                            case OmniMouse.Switching.Direction.Right:
                                // Exited right edge, enter at left edge of remote screen
                                _remoteCursorX = remoteLeft;
                                _remoteCursorY = entryY;
                                break;
                            case OmniMouse.Switching.Direction.Left:
                                // Exited left edge, enter at right edge of remote screen
                                _remoteCursorX = remoteRight - 1;
                                _remoteCursorY = entryY;
                                break;
                            case OmniMouse.Switching.Direction.Down:
                                // Exited bottom edge, enter at top edge of remote screen
                                _remoteCursorX = entryX;
                                _remoteCursorY = remoteTop;
                                break;
                            case OmniMouse.Switching.Direction.Up:
                                // Exited top edge, enter at bottom edge of remote screen
                                _remoteCursorX = entryX;
                                _remoteCursorY = remoteBottom - 1;
                                break;
                        }
                        Console.WriteLine($"[HOOK][Stream] Remote streaming ENABLED (dir={direction}) - Remote cursor initialized to ({_remoteCursorX},{_remoteCursorY}) in remote space (bounds: L={remoteLeft},T={remoteTop},R={remoteRight},B={remoteBottom})");

                        // Send absolute position to remote to ensure synchronization
                        if (_instance?._udpTransmitter != null)
                        {
                            _instance._udpTransmitter.SendMouse(_remoteCursorX, _remoteCursorY, isDelta: false);
                            Console.WriteLine($"[HOOK][Stream] Sent absolute position ({_remoteCursorX},{_remoteCursorY}) to remote for sync");
                        }
                    }
                    else
                    {
                        // Fallback: no remote bounds found or no direction
                        _remoteCursorX = entryX;
                        _remoteCursorY = entryY;
                        Console.WriteLine($"[HOOK][Stream] Remote streaming ENABLED (dir={direction}) - WARNING: Using local coordinates as fallback ({_remoteCursorX},{_remoteCursorY})");
                    }
                }
                else
                {
                    // No monitors available
                    _remoteCursorX = cur.x;
                    _remoteCursorY = cur.y;
                    Console.WriteLine($"[HOOK][Stream] Remote streaming ENABLED (dir={direction}) - WARNING: No monitors available, using local coords ({_remoteCursorX},{_remoteCursorY})");
                }
            }
            else
            {
                _remoteCursorX = 0;
                _remoteCursorY = 0;
                Console.WriteLine($"[HOOK][Stream] Remote streaming ENABLED (dir={direction}) - WARNING: No cursor position or remote peer available");
            }
        }

        public static void EndRemoteStreaming()
        {
            _remoteStreaming = false;
            _remoteStreamingDirection = null;
            _remoteStreamingReleaseAccum = 0;
            _remoteCursorX = 0;
            _remoteCursorY = 0;
            Console.WriteLine("[HOOK][Stream] Remote streaming DISABLED");
            
            // Reset to Receiver role so we can initiate another edge crossing
            if (_instance?._udpTransmitter is UdpMouseTransmitter tx)
            {
                tx.SetLocalRole(ConnectionRole.Receiver);
                Console.WriteLine("[HOOK][Stream] Role reset to Receiver - ready for next edge crossing");
            }
        }

        /// <summary>
        /// Fully resets all peer connection state. Called when peer disconnects.
        /// This ensures no stale state causes issues after reconnection.
        /// </summary>
        public static void ResetPeerConnectionState()
        {
            Console.WriteLine("[HOOK] Resetting all peer connection state");
            
            // End any active streaming
            _remoteStreaming = false;
            _remoteStreamingDirection = null;
            _remoteStreamingReleaseAccum = 0;
            _remoteCursorX = 0;
            _remoteCursorY = 0;
            
            // Reset edge detection state
            _receiverReportedEdgeHit = false;
            
            // Clear remote peer ID so edge detection won't try to use stale data
            RemotePeerClientId = null;
            
            // Reset preflight state
            lock (_preFlightLock)
            {
                _preFlightAckReceived = false;
            }
            
            // Reset suppression state
            lock (_suppressionLock)
            {
                _suppressX = int.MinValue;
                _suppressY = int.MinValue;
                _suppressCount = 0;
            }
            
            Console.WriteLine("[HOOK] Peer connection state fully reset");
        }

        public static void SuppressNextMoveFrom(int x, int y)
        {
            lock (_suppressionLock)
            {
                Console.WriteLine($"[HOOK][Suppress] Arm next-move suppression for ({x},{y}). prev=({_suppressX},{_suppressY}), prevCount={_suppressCount} -> newCount=2");
                _suppressX = x;
                _suppressY = y;
                _suppressCount = 2; // tolerate du  plicate low-level events
            }
        }

        static InputHooks()
        {
            _kbProc = KbHookCallback;
            _mouseProc = MouseHookCallback;
        }

        // IMPORTANT: InputCoordinator is now required. This class no longer uses CoordinateNormalizer or any legacy normalization.
        public InputHooks(IUdpMouseTransmitter udpTransmitter, InputCoordinator inputCoordinator, IMultiMachineSwitcher? switcher = null)
        {
            _udpTransmitter = udpTransmitter ?? throw new ArgumentNullException(nameof(udpTransmitter));
            _inputCoordinator = inputCoordinator ?? throw new ArgumentNullException(nameof(inputCoordinator));
            _switcher = switcher; // Optional: new switching engine
            _instance = this;

            // Subscribe to role changes (when available) to enforce role-gated behavior
            if (_udpTransmitter is UdpMouseTransmitter concrete)
            {
                concrete.RoleChanged += OnRoleChanged;
            }

            // initialize last seen position to current cursor position
            lock (_sendGate)
            {
                if (GetCursorPosImpl(out var p))
                {
                    _lastMouseX = p.x;
                    _lastMouseY = p.y;
                    _lastActualCursorX = p.x;
                    _lastActualCursorY = p.y;
                }
                else
                {
                    _lastMouseX = 0;
                    _lastMouseY = 0;
                    _lastActualCursorX = 0;
                    _lastActualCursorY = 0;
                }
            }
        }

        private void OnRoleChanged(ConnectionRole newRole)
        {
            lock (_sendGate)
            {
                if (newRole == ConnectionRole.Sender)
                {
                    // Clear suppression on transition to Sender to avoid stale state
                    lock (_suppressionLock)
                    {
                        _suppressCount = 0;
                        _suppressX = int.MinValue;
                        _suppressY = int.MinValue;
                    }
                    Console.WriteLine("[HOOK][Role] Switched to Sender; suppression cleared and send path enabled.");
                }
                else
                {
                    Console.WriteLine("[HOOK][Role] Switched to Receiver; send path gated.");
                }
            }
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

            Console.WriteLine("[HOOK] Hooks installed (keyboard and mouse).");
        }

        public void UninstallHooks()
        {
            if (_kbHook != IntPtr.Zero) { UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
            if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }

            // clear suppression and one-time flags to avoid leakage across sessions
            _suppressCount = 0;
            _suppressX = int.MinValue;
            _suppressY = int.MinValue;
            _loggedFirstMouseCallback = false;

            // ask the pump thread to exit cleanly so the thread doesn't linger
            var tid = _messageThreadId;
            if (tid != 0)
            {
                try
                {
                    if (PostThreadMessage(tid, WM_QUIT, UIntPtr.Zero, IntPtr.Zero))
                        Console.WriteLine("[HOOK] Posted WM_QUIT to message pump thread.");
                    else
                        Console.WriteLine("[HOOK] Warning: failed to post WM_QUIT (thread may not have a queue yet).");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HOOK] Failed to post WM_QUIT: {ex.Message}");
                }
            }

            Console.WriteLine("[HOOK] Hooks uninstalled. Suppression state cleared.");
        }

        public void RunMessagePump()
        {
            // Capture our thread id so UninstallHooks can stop this loop
            _messageThreadId = GetCurrentThreadId();

            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            // clear when exiting so future sessions can reuse
            _messageThreadId = 0;
            Console.WriteLine("[HOOK] Message pump exited.");
        }

        private static bool Ctrl => (GetKeyState(0x11) & 0x8000) != 0; // VK_CONTROL
        private static bool Shift => (GetKeyState(0x10) & 0x8000) != 0; // VK_SHIFT

        private static bool IsInjectedMouse(int flags) =>
            (flags & (LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED)) != 0;

        private static bool IsInjectedKey(int flags) =>
            (flags & (LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED)) != 0;

        private static void NotifyLocalMouseActivitySafe()
        {
            try
            {
                _instance?.LocalMouseActivity?.Invoke();
            }
            catch (Exception ex)
            {
                // Avoid destabilizing the hook chain due to subscriber exceptions.
                Console.WriteLine($"[HOOK] LocalMouseActivity handler error: {ex.Message}");
            }
        }

        /// <summary>
        /// Injects a relative mouse movement using SendInput.
        /// CRITICAL: Sets _isSyntheticInput flag BEFORE calling SendInput so our hook bypasses it.
        /// </summary>
        /// <param name="deltaX">Horizontal mouse movement in pixels (positive = right, negative = left)</param>
        /// <param name="deltaY">Vertical mouse movement in pixels (positive = down, negative = up)</param>
        public static void InjectMouseDelta(int deltaX, int deltaY)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                u = new INPUT_UNION
                {
                    mi = new MOUSEINPUT
                    {
                        dx = deltaX,
                        dy = deltaY,
                        mouseData = 0,
                        dwFlags = MOUSEEVENTF_MOVE, // Relative movement (NOT absolute)
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            // CRITICAL: Set flag BEFORE SendInput so hook allows it through
            _isSyntheticInput = true;

            uint result = SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
            
            if (result == 0)
            {
                Console.WriteLine($"[HOOK][InjectDelta] SendInput failed for delta ({deltaX},{deltaY})");
            }
            else
            {
                Console.WriteLine($"[HOOK][InjectDelta] Injected delta ({deltaX},{deltaY})");
            }
        }

        public static void InjectMouseButton(OmniMouse.Network.MouseButtonNet button, bool isDown)
        {
            int flags = button switch
            {
                OmniMouse.Network.MouseButtonNet.Left => isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP,
                OmniMouse.Network.MouseButtonNet.Right => isDown ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
                OmniMouse.Network.MouseButtonNet.Middle => isDown ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
                _ => 0
            };

            if (flags == 0)
                return;

            var input = new INPUT
            {
                type = INPUT_MOUSE,
                u = new INPUT_UNION
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        mouseData = 0,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            _isSyntheticInput = true;
            uint result = SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
            if (result == 0)
            {
                Console.WriteLine($"[HOOK][InjectBtn] SendInput failed for {button} {(isDown ? "DOWN" : "UP")}");
            }
            else
            {
                Console.WriteLine($"[HOOK][InjectBtn] Injected {button} {(isDown ? "DOWN" : "UP")}");
            }
        }

        public static void InjectMouseWheel(int delta)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                u = new INPUT_UNION
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        mouseData = delta,
                        dwFlags = MOUSEEVENTF_WHEEL,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            _isSyntheticInput = true;
            uint result = SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
            if (result == 0)
            {
                Console.WriteLine($"[HOOK][InjectWheel] SendInput failed for delta {delta}");
            }
            else
            {
                Console.WriteLine($"[HOOK][InjectWheel] Injected delta {delta}");
            }
        }

        /// <summary>
        /// Query the current role from UdpMouseTransmitter. DRY principle - single source of truth.
        /// </summary>
        private static ConnectionRole GetCurrentRole()
        {
            if (_instance?._udpTransmitter is UdpMouseTransmitter udp)
            {
                // Access the internal role via reflection or public property if exposed
                // For now, assume we add a public getter to UdpMouseTransmitter
                return udp.CurrentRole;
            }
            // Default to Receiver if transmitter not available (safe fallback)
            return ConnectionRole.Receiver;
        }
    }
}