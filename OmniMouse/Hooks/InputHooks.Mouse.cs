using System;
using System.Runtime.InteropServices;
using OmniMouse.Network;

namespace OmniMouse.Hooks
{
    public partial class InputHooks
    {
        private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // First-hit log to confirm callback is actually firing
            if (!_loggedFirstMouseCallback)
            {
                _loggedFirstMouseCallback = true;
                //Console.WriteLine("[HOOK][Mouse] MouseHookCallback entered (first invocation).");//debug
            }

            // This prevents us from blocking our own SendInput() calls
            if (_isSyntheticInput)
            {
                _isSyntheticInput = false; // Reset flag
                //Console.WriteLine("[HOOK][Mouse] Bypassing synthetic input");
                if (_instance != null)
                    return CallNextHookExImpl(_instance._mouseHook, nCode, wParam, lParam);
                return IntPtr.Zero;
            }

            if (nCode >= 0 && _instance != null)
            {
                var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                // Query role (still used to gate network send path), but edge detection
                // can now occur while Receiver to allow claiming Sender dynamically.
                var currentRole = GetCurrentRole();

                switch ((int)wParam)
                {
                    case WM_MOUSEMOVE:
                        // Always ignore injected events regardless of role
                        if (IsInjectedMouse(ms.flags))
                        {
                            // Console.WriteLine($"[MOVE][Injected] ({ms.pt.x}, {ms.pt.y}) suppressed.");

                            TryEdgeReturn(ms.pt.x, ms.pt.y);
                            break;
                        }

                        // Suppression trace (coordinates-based) to catch SetCursorPos-style echoes.
                        bool hadSuppression = false;
                        bool suppressed = false;
                        int afterCount = 0;
                        int supX = int.MinValue, supY = int.MinValue;
                        lock (_suppressionLock)
                        {
                            if (_suppressCount > 0)
                            {
                                hadSuppression = true;
                                if (ms.pt.x == _suppressX && ms.pt.y == _suppressY)
                                {
                                    afterCount = --_suppressCount;
                                    suppressed = true;
                                }
                                else
                                {
                                    supX = _suppressX;
                                    supY = _suppressY;
                                    afterCount = _suppressCount;
                                }
                            }
                        }
                        if (suppressed)
                        {
                            //Console.WriteLine($"[HOOK][Suppress] MATCH at ({ms.pt.x},{ms.pt.y}). count->{afterCount}");
                            if (afterCount == 0)
                            {
                                //Console.WriteLine("[HOOK][Suppress] Cleared.");
                            }
                            break; // do not send
                        }
                        else if (hadSuppression)
                        {
                            //Console.WriteLine($"[HOOK][Suppress] PENDING for ({supX},{supY}), saw ({ms.pt.x},{ms.pt.y}). count={afterCount} (no decrement)");
                        }

                        // Previously we ignored movement entirely when not Sender.
                        // In seamless mode we still invoke edge policy evaluation so a Receiver
                        // can trigger a take-control (sender claim) via NetworkSwitchCoordinator.
                        bool isSender = currentRole == ConnectionRole.Sender;

                        //role-gated send path: guard with send gate and re-check role inside
                        lock (_sendGate)
                        {
                            if (!isSender && !_remoteStreaming)
                            {
                                // EDGE CLAIM LOGIC (Receiver attempting to become Sender)
                                TryEdgeClaim(ms.pt.x, ms.pt.y);
                                // Evaluate edge switching while Receiver (existing path)
                                if (_instance._switcher != null)
                                {
                                    _instance._switcher.OnMouseMove(ms.pt.x, ms.pt.y);
                                }
                                else
                                {
                                    // Debug: log when switcher is not available
                                    if (!_loggedMissingSwitcher)
                                    {
                                        _loggedMissingSwitcher = true;
                                        Console.WriteLine("[HOOK][Mouse][WARN] Receiver mode: switcher is null, cannot evaluate edge switching");
                                    }
                                }
                                break; // no sending while Receiver (until streaming starts)
                            }
                            else if (GetCurrentRole() != ConnectionRole.Sender && _remoteStreaming)
                            {
                                // Lost sender role mid-stream; terminate streaming.
                                EndRemoteStreaming();
                                break;
                            }

                            // Commented out to avoid console flooding which can cause mouse lag.
                            // Console.WriteLine($"[MOVE][Sender] ({ms.pt.x}, {ms.pt.y})");

                            // This is definitively local input; notify observers (e.g., to assume 'sender' role).
                            NotifyLocalMouseActivitySafe();

                            try
                            {
                                // If remote streaming is enabled, forward DELTAS to peer and BLOCK local input
                                if (_remoteStreaming)
                                {
                                    // Calculate deltas from ACTUAL cursor baseline (stable while blocked)
                                    // Using _lastMouseX (raw advancing hook baseline) causes acceleration artifacts.
                                    int dx = ms.pt.x - _instance._lastActualCursorX;
                                    int dy = ms.pt.y - _instance._lastActualCursorY;

                                    // Send raw deltas (transmitter applies sentinel). We deliberately avoid
                                    // updating the baseline to the raw hook coordinates; instead we refresh from
                                    // the real cursor position after sending to keep future deltas accurate.
                                    // Remote streaming release detection: accumulate opposite-direction movement
                                    if (_remoteStreamingDirection.HasValue)
                                    {
                                        switch (_remoteStreamingDirection.Value)
                                        {
                                            case OmniMouse.Switching.Direction.Left: // exited via left edge; return by moving RIGHT (dx > 0)
                                                _remoteStreamingReleaseAccum = dx > 0 ? _remoteStreamingReleaseAccum + dx : 0;
                                                break;
                                            case OmniMouse.Switching.Direction.Right: // exited via right edge; return by moving LEFT (dx < 0)
                                                _remoteStreamingReleaseAccum = dx < 0 ? _remoteStreamingReleaseAccum + (-dx) : 0;
                                                break;
                                            case OmniMouse.Switching.Direction.Up: // exited via top; return by moving DOWN (dy > 0)
                                                _remoteStreamingReleaseAccum = dy > 0 ? _remoteStreamingReleaseAccum + dy : 0;
                                                break;
                                            case OmniMouse.Switching.Direction.Down: // exited via bottom; return by moving UP (dy < 0)
                                                _remoteStreamingReleaseAccum = dy < 0 ? _remoteStreamingReleaseAccum + (-dy) : 0;
                                                break;
                                        }
                                        // if (_remoteStreamingReleaseAccum >= RemoteReleaseThresholdPixels)
                                        // {
                                        //     Console.WriteLine("[HOOK][Stream] Release threshold met; ending remote streaming.");
                                        //     EndRemoteStreaming();
                                        //     // Allow local input to resume; do not block.
                                        //     break;
                                        // }
                                    }

                                    _instance._udpTransmitter.SendMouse(dx, dy, isDelta: true);

                                    // Refresh actual cursor position (it should be unchanged, but reconcile drift).
                                    if (GetCursorPos(out var cur))
                                    {
                                        _instance._lastActualCursorX = cur.x;
                                        _instance._lastActualCursorY = cur.y;
                                    }
                                    
                                    // CRITICAL: Return 1 to BLOCK this input locally
                                    // The mouse will appear stuck on PC1, but PC2 will move based on deltas
                                    // Console.WriteLine($"[MOVE][RemoteStream] Sent delta ({dx},{dy}), BLOCKING local input");
                                    if (_remoteStreaming)
                                    {
                                        return new IntPtr(1); // still streaming; block local input
                                    }
                                    // streaming ended this iteration; fall through allowing local input
                                }
                                else
                                {
                                    // Use the MultiMachineSwitcher if available (edge-detect & decide switches)
                                    if (_instance._switcher != null)
                                    {
                                        _instance._switcher.OnMouseMove(ms.pt.x, ms.pt.y);
                                    }
                                    else
                                    {
                                        // Fallback: existing delta-based logic
                                        int dx = ms.pt.x - _instance._lastMouseX;
                                        int dy = ms.pt.y - _instance._lastMouseY;
                                        _instance._inputCoordinator.OnMouseInput(dx, dy);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                //Console.WriteLine($"[HOOK] Failed to forward mouse to switcher/coordinator: {ex.Message}");
                            }
                            finally
                            {
                                // Update last seen position after processing (if not in remote streaming mode)
                                if (!_remoteStreaming)
                                {
                                    _instance._lastMouseX = ms.pt.x;
                                    _instance._lastMouseY = ms.pt.y;
                                    // Keep actual baseline in sync when not streaming
                                    _instance._lastActualCursorX = ms.pt.x;
                                    _instance._lastActualCursorY = ms.pt.y;
                                }
                            }
                        }
                        break;

                    case WM_LBUTTONDOWN:
                    case WM_RBUTTONDOWN:
                    case WM_MBUTTONDOWN:
                        if (!IsInjectedMouse(ms.flags))
                        {
                            var (btn, btnName) = (int)wParam switch
                            {
                                WM_LBUTTONDOWN => (MouseButtonNet.Left, "LBTN"),
                                WM_RBUTTONDOWN => (MouseButtonNet.Right, "RBTN"),
                                WM_MBUTTONDOWN => (MouseButtonNet.Middle, "MBTN"),
                                _ => (MouseButtonNet.Left, "BTN")
                            };

                            if (currentRole != ConnectionRole.Sender)
                            {
                                //Console.WriteLine($"[{btnName}][Receiver] ({ms.pt.x}, {ms.pt.y}) - ignored (not Sender)");
                            }
                            else
                            {
                                //Console.WriteLine($"[{btnName}][Sender] DOWN at ({ms.pt.x}, {ms.pt.y})");
                                NotifyLocalMouseActivitySafe();
                                if (_remoteStreaming)
                                {
                                    _instance._udpTransmitter.SendMouseButton(btn, isDown: true);
                                    //Console.WriteLine($"[{btnName}][RemoteStream] Sent DOWN, BLOCKING local input");
                                    return new IntPtr(1);
                                }
                            }
                        }
                        break;

                    case WM_LBUTTONUP:
                    case WM_RBUTTONUP:
                    case WM_MBUTTONUP:
                        if (!IsInjectedMouse(ms.flags))
                        {
                            var (btn, btnName) = (int)wParam switch
                            {
                                WM_LBUTTONUP => (MouseButtonNet.Left, "LBTN"),
                                WM_RBUTTONUP => (MouseButtonNet.Right, "RBTN"),
                                WM_MBUTTONUP => (MouseButtonNet.Middle, "MBTN"),
                                _ => (MouseButtonNet.Left, "BTN")
                            };

                            if (currentRole != ConnectionRole.Sender)
                            {
                                //Console.WriteLine($"[{btnName}][Receiver] ({ms.pt.x}, {ms.pt.y}) - ignored (not Sender)");
                            }
                            else
                            {
                                //Console.WriteLine($"[{btnName}][Sender] UP at ({ms.pt.x}, {ms.pt.y})");
                                NotifyLocalMouseActivitySafe();
                                if (_remoteStreaming)
                                {
                                    _instance._udpTransmitter.SendMouseButton(btn, isDown: false);
                                    //Console.WriteLine($"[{btnName}][RemoteStream] Sent UP, BLOCKING local input");
                                    return new IntPtr(1);
                                }
                            }
                        }
                        break;

                    case WM_MOUSEWHEEL:
                        if (!IsInjectedMouse(ms.flags))
                        {
                            int delta = (short)((ms.mouseData >> 16) & 0xffff);
                            
                            if (currentRole != ConnectionRole.Sender)
                            {
                                //Console.WriteLine($"[WHEEL][Receiver] delta={delta} at ({ms.pt.x}, {ms.pt.y}) - ignored (not Sender)");
                            }
                            else
                            {
                                            //Console.WriteLine($"[WHEEL][Sender] delta={delta} at ({ms.pt.x}, {ms.pt.y})");
                                NotifyLocalMouseActivitySafe();
                                if (_remoteStreaming)
                                {
                                    _instance._udpTransmitter.SendMouseWheel(delta);
                                    //Console.WriteLine("[WHEEL][RemoteStream] Sent, BLOCKING local input");
                                    return new IntPtr(1);
                                }
                            }
                        }
                        break;
                }
            }
            
            if (_instance != null)
                return CallNextHookExImpl(_instance._mouseHook, nCode, wParam, lParam);
            return IntPtr.Zero;
        }



        // ---------------------------------------------------------
        // NEW: Dedicated Method for Returning to Local Control
        // ---------------------------------------------------------
        private static void TryEdgeReturn(int x, int y)
        {
            // Get Local Bounds
            if (_instance?._inputCoordinator == null) return;
            var monitors = _instance._inputCoordinator.ScreenMap?.GetMonitorsSnapshot();
            if (monitors == null || monitors.Count == 0) return;

            int left = int.MaxValue, top = int.MaxValue;
            int right = int.MinValue, bottom = int.MinValue;

            foreach (var monitor in monitors)
            {
                if (monitor.OwnerClientId == _instance._inputCoordinator.SelfClientId)
                {
                    var bounds = monitor.LocalBounds;
                    left = Math.Min(left, bounds.Left);
                    top = Math.Min(top, bounds.Top);
                    right = Math.Max(right, bounds.Right);
                    bottom = Math.Max(bottom, bounds.Bottom);
                }
            }
            if (left == int.MaxValue) return;

            // Check if we hit the edges
            bool hitRight = (right - x) <= EdgeThresholdPixels;
            bool hitLeft = (x - left) <= EdgeThresholdPixels;
            bool hitTop = (y - top) <= EdgeThresholdPixels;
            bool hitBottom = (bottom - y) <= EdgeThresholdPixels;

            // Simple logic: If we hit ANY edge while streaming, we likely want to return.
            // You can make this stricter (e.g. only the edge facing the local user) if needed.
            if (hitRight || hitLeft || hitTop || hitBottom)
            {
                Console.WriteLine("[HOOK][EdgeReturn] Edge hit detected during stream. Returning to local.");
                EndRemoteStreaming();
            }
        }




        private static void TryEdgeClaim(int x, int y)
        {
            if (_instance?._udpTransmitter is not UdpMouseTransmitter tx) return;
            if (!tx.HandshakeComplete) return;
            if (tx.CurrentRole != ConnectionRole.Receiver) return;
            if (string.IsNullOrEmpty(RemotePeerClientId)) return;
            if (_instance?._inputCoordinator == null) return;

            // Use VirtualScreenMap to get the actual screen bounds
            var monitors = _instance._inputCoordinator.ScreenMap?.GetMonitorsSnapshot();
            if (monitors == null || monitors.Count == 0) return; // FIXED: Length -> Count

            // Find the local monitor bounds (monitors owned by this client)
            int left = int.MaxValue;
            int top = int.MaxValue;
            int right = int.MinValue;
            int bottom = int.MinValue;

            foreach (var monitor in monitors)
            {
                if (monitor.OwnerClientId == _instance._inputCoordinator.SelfClientId)
                {
                    var bounds = monitor.LocalBounds;
                    left = Math.Min(left, bounds.Left);
                    top = Math.Min(top, bounds.Top);
                    right = Math.Max(right, bounds.Right);
                    bottom = Math.Max(bottom, bounds.Bottom);
                }
            }

            // If we couldn't find any monitors, bail out
            if (left == int.MaxValue) return;

            bool nearRight = (right - x) <= EdgeThresholdPixels;
            bool nearLeft = (x - left) <= EdgeThresholdPixels;

            // Assuming two-machine horizontal layout: claim when exiting toward peer.
            if (nearRight)
            {
                Console.WriteLine("[HOOK][EdgeClaim] Right edge hit; attempting sender claim...");
                BeginRemoteStreaming(OmniMouse.Switching.Direction.Right);
                try
                {
                    tx.SendTakeControl(RemotePeerClientId!, x, y);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HOOK][EdgeClaim] SendTakeControl failed: {ex.Message}");
                    EndRemoteStreaming();
                }
            }
            else if (nearLeft)
            {
                Console.WriteLine("[HOOK][EdgeClaim] Left edge hit; attempting sender claim...");
                BeginRemoteStreaming(OmniMouse.Switching.Direction.Left);
                try
                {
                    tx.SendTakeControl(RemotePeerClientId!, x, y);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HOOK][EdgeClaim] SendTakeControl failed: {ex.Message}");
                    EndRemoteStreaming();
                }
            }
        }
    }
}