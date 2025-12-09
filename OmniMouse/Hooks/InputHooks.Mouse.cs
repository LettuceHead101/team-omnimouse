using System;
using System.Runtime.InteropServices;
using OmniMouse.Network;

namespace OmniMouse.Hooks
{
    public partial class InputHooks
    {
        // Pre-flight verification constants
        private const int EdgeClaimDebounceMs = 150;
        private const int PreFlightTimeoutMs = 250;
        private static DateTime _lastEdgeClaimAttempt = DateTime.MinValue;
        private static volatile bool _preFlightAckReceived = false;
        private static readonly object _preFlightLock = new object();

        // Message type for pre-flight verification
        private const byte MSG_PREFLIGHT_REQUEST = 0x20;
        private const byte MSG_PREFLIGHT_ACK = 0x21;
        // Add new message type for edge return negotiation
        private const byte MSG_RECEIVER_EDGE_HIT = 0x22;  // NEW

        // Add callback flag
        private static volatile bool _receiverReportedEdgeHit = false;

        /// <summary>
        /// Test seam for SendPreFlightRequest. Tests can replace this to bypass network calls.
        /// </summary>
        internal static Func<UdpMouseTransmitter, bool>? SendPreFlightRequestImpl = null;

        /// <summary>
        /// Test seam for WaitForPreFlightAck. Tests can replace this to return immediately.
        /// </summary>
        internal static Func<int, bool>? WaitForPreFlightAckImpl = null;

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

                            // TryEdgeReturn(ms.pt.x, ms.pt.y);
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

                                    // Send deltas (remote OS may clamp at physical edges)
                                    _instance._udpTransmitter.SendMouse(dx, dy, isDelta: true);

                                    // Accumulate deltas to track remote cursor position, but do NOT allow buffer past the edge
                                    if (TryGetRemoteBounds(out int rLeft, out int rTop, out int rRight, out int rBottom))
                                    {
                                        int nextRemoteX = _remoteCursorX + dx;
                                        int nextRemoteY = _remoteCursorY + dy;

                                        // Only apply dx if it would keep us within bounds
                                        if ((dx < 0 && _remoteCursorX > rLeft) || (dx > 0 && _remoteCursorX < rRight - 1))
                                        {
                                            // Clamp to bounds
                                            if (nextRemoteX < rLeft) nextRemoteX = rLeft;
                                            if (nextRemoteX > rRight - 1) nextRemoteX = rRight - 1;
                                            _remoteCursorX = nextRemoteX;
                                        }
                                        // else: ignore dx that would move us out of bounds

                                        // Only apply dy if it would keep us within bounds
                                        if ((dy < 0 && _remoteCursorY > rTop) || (dy > 0 && _remoteCursorY < rBottom - 1))
                                        {
                                            if (nextRemoteY < rTop) nextRemoteY = rTop;
                                            if (nextRemoteY > rBottom - 1) nextRemoteY = rBottom - 1;
                                            _remoteCursorY = nextRemoteY;
                                        }
                                        // else: ignore dy that would move us out of bounds
                                    }
                                    else
                                    {
                                        // Fallback: just accumulate
                                        _remoteCursorX += dx;
                                        _remoteCursorY += dy;
                                    }

                                    // Check if remote cursor is trying to return home by hitting opposite edge
                                    TryEdgeReturn(_remoteCursorX, _remoteCursorY);


                                    // Refresh actual cursor position (it should be unchanged, but reconcile drift).
                                    if (GetCursorPosImpl(out var cur))
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
        private static void TryEdgeReturn(int senderX, int senderY)
        {
            // Guard: Must be actively streaming with a known direction
            if (!_remoteStreaming || !_remoteStreamingDirection.HasValue)
            {
                return;
            }

            // Guard: Receiver must have reported edge hit
            if (!_receiverReportedEdgeHit)
            {
                // Receiver hasn't confirmed yet - don't return
                return;
            }

            // Get remote bounds to check if sender's remote cursor is at return edge
            if (!TryGetRemoteBounds(out int rLeft, out int rTop, out int rRight, out int rBottom))
            {
                Console.WriteLine("[HOOK][EdgeReturn] Cannot get remote bounds - aborting edge return");
                return;
            }

            // IMPORTANT: rRight and rBottom from TryGetRemoteBounds are EXCLUSIVE bounds
            // (calculated as gb.X + gb.Width and gb.Y + gb.Height)
            // The actual rightmost pixel is at (rRight - 1), bottommost at (rBottom - 1)
            int rRightInclusive = rRight - 1;
            int rBottomInclusive = rBottom - 1;

            // Determine if Sender's remote cursor is at the RETURN EDGE (opposite of entry direction)
            bool senderAtReturnEdge = false;
            string returnEdgeDesc = "";

            switch (_remoteStreamingDirection.Value)
            {
                case OmniMouse.Switching.Direction.Right:
                    // Entered via right edge; return by hitting LEFT edge
                    // Left edge check: distance from leftmost pixel (rLeft is inclusive)
                    senderAtReturnEdge = (senderX - rLeft) <= EdgeThresholdPixels;
                    returnEdgeDesc = "LEFT";
                    break;

                case OmniMouse.Switching.Direction.Left:
                    // Entered via left edge; return by hitting RIGHT edge
                    // Right edge check: distance from rightmost pixel (rRightInclusive)
                    senderAtReturnEdge = (rRightInclusive - senderX) <= EdgeThresholdPixels;
                    returnEdgeDesc = "RIGHT";
                    break;

                case OmniMouse.Switching.Direction.Up:
                    // Entered via top edge; return by hitting BOTTOM edge
                    // Bottom edge check: distance from bottommost pixel (rBottomInclusive)
                    senderAtReturnEdge = (rBottomInclusive - senderY) <= EdgeThresholdPixels;
                    returnEdgeDesc = "BOTTOM";
                    break;

                case OmniMouse.Switching.Direction.Down:
                    // Entered via bottom edge; return by hitting TOP edge
                    // Top edge check: distance from topmost pixel (rTop is inclusive)
                    senderAtReturnEdge = (senderY - rTop) <= EdgeThresholdPixels;
                    returnEdgeDesc = "TOP";
                    break;
            }

            if (!senderAtReturnEdge)
            {
                // Sender's remote cursor not yet at return edge
                Console.WriteLine($"[HOOK][EdgeReturn] Receiver at edge, but Sender remote cursor at ({senderX},{senderY}) not at {returnEdgeDesc} edge yet. Bounds=({rLeft},{rTop})-({rRightInclusive},{rBottomInclusive})");
                return;
            }

            // Check accumulator threshold (smooth out jitter)
            if (_remoteStreamingReleaseAccum < RemoteReleaseThresholdPixels)
            {
                Console.WriteLine($"[HOOK][EdgeReturn] Both at edge, but accumulator only at {_remoteStreamingReleaseAccum}px (need {RemoteReleaseThresholdPixels}px)");
                return;
            }

            // SUCCESS: Both Sender and Receiver agree they're at the return edge
            Console.WriteLine($"[HOOK][EdgeReturn] MUTUAL AGREEMENT! Sender remote at ({senderX},{senderY}), Receiver also at return edge. Accumulator: {_remoteStreamingReleaseAccum}px. ENDING STREAM");
            
            // Reset flags
            _receiverReportedEdgeHit = false;
            _remoteStreamingReleaseAccum = 0;
            
            EndRemoteStreaming();
        }

        // Helper: get remote machine aggregated bounds
        private static bool TryGetRemoteBounds(out int left, out int top, out int right, out int bottom)
        {
            left = int.MaxValue; top = int.MaxValue; right = int.MinValue; bottom = int.MinValue;
            if (_instance?._inputCoordinator == null || string.IsNullOrEmpty(RemotePeerClientId))
                return false;
            var monitors = _instance._inputCoordinator.ScreenMap?.GetMonitorsSnapshot();
            if (monitors == null || monitors.Count == 0) return false;
            foreach (var monitor in monitors)
            {
                if (monitor.OwnerClientId == RemotePeerClientId)
                {
                    var gb = monitor.GlobalBounds;
                    int gLeft = gb.X;
                    int gTop = gb.Y;
                    int gRight = gb.X + gb.Width;
                    int gBottom = gb.Y + gb.Height;
                    if (gLeft < left) left = gLeft;
                    if (gTop < top) top = gTop;
                    if (gRight > right) right = gRight;
                    if (gBottom > bottom) bottom = gBottom;
                }
            }
            return left != int.MaxValue;
        }


        private static void TryEdgeClaim(int x, int y)
        {
            if (_instance?._udpTransmitter is not UdpMouseTransmitter tx) return;
            if (!tx.HandshakeComplete) return;
            if (tx.CurrentRole != ConnectionRole.Receiver) return;
            if (string.IsNullOrEmpty(RemotePeerClientId)) return;
            if (_instance?._inputCoordinator == null) return;

            // 1. DEBOUNCE CHECK: Prevent rapid re-attempts
            var now = DateTime.UtcNow;
            if ((now - _lastEdgeClaimAttempt).TotalMilliseconds < EdgeClaimDebounceMs)
            {
                //Console.WriteLine($"[HOOK][EdgeClaim] Debounce active - only {(now - _lastEdgeClaimAttempt).TotalMilliseconds:F0}ms since last attempt");
                return;
            }
            _lastEdgeClaimAttempt = now;

            // 2. VERIFICATION REQUEST: Send preflight check to Sender (use test seam if available)
            //Console.WriteLine($"[HOOK][EdgeClaim] Sending preflight verification to {RemotePeerClientId}");
            var preflightSendFunc = SendPreFlightRequestImpl ?? SendPreFlightRequest;
            if (!preflightSendFunc(tx))
            {
                Console.WriteLine("[HOOK][EdgeClaim] Failed to send preflight request");
                return;
            }

            // 3. WAIT FOR ACK: Block up to 250ms for response (use test seam if available)
            var preflightWaitFunc = WaitForPreFlightAckImpl ?? WaitForPreFlightAck;
            if (!preflightWaitFunc(PreFlightTimeoutMs))
            {
                Console.WriteLine($"[HOOK][EdgeClaim] Preflight timeout after {PreFlightTimeoutMs}ms - aborting switch");
                return;
            }

            //Console.WriteLine("[HOOK][EdgeClaim] Preflight ACK received - proceeding with edge claim");

            // 4. PROCEED WITH EXISTING LOGIC
            // Snapshot Monitors
            var monitors = _instance._inputCoordinator.ScreenMap?.GetMonitorsSnapshot();
            if (monitors == null || monitors.Count == 0) return;

            // Find Local Bounds (Global)
            int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
            foreach (var monitor in monitors)
            {
                if (monitor.OwnerClientId == _instance._inputCoordinator.SelfClientId)
                {
                    var gb = monitor.GlobalBounds;
                    left = Math.Min(left, gb.X);
                    top = Math.Min(top, gb.Y);
                    right = Math.Max(right, gb.X + gb.Width);
                    bottom = Math.Max(bottom, gb.Y + gb.Height);
                }
            }
            if (left == int.MaxValue) return;

            // Check Local Edges
            // IMPORTANT: 'right' and 'bottom' are EXCLUSIVE bounds (gb.X + gb.Width, gb.Y + gb.Height)
            // The actual rightmost pixel is at (right - 1), bottommost at (bottom - 1)
            int rightInclusive = right - 1;
            int bottomInclusive = bottom - 1;
            
            bool nearRight = (rightInclusive - x) <= EdgeThresholdPixels;
            bool nearLeft = (x - left) <= EdgeThresholdPixels;
            bool nearTop = (y - top) <= EdgeThresholdPixels;
            bool nearBottom = (bottomInclusive - y) <= EdgeThresholdPixels;

            // --- HELPER: RESOLVE NEIGHBOR ---
            (bool hasLayout, string? neighborId) ResolveNeighbor(OmniMouse.Switching.Direction dir)
            {
                var coord = tx.GetLayoutCoordinator();
                if (coord == null) return (false, null);
                var layout = coord.CurrentLayout;
                var localId = tx.GetLocalMachineId();
                var me = layout.Machines.FirstOrDefault(m => m.MachineId == localId);
                if (me == null || !me.IsPositioned) return (true, null);

                // Use grid-based neighbor lookup for 4-directional support
                var neighbor = layout.GetNeighbor(me, dir);
                return (true, neighbor?.MachineId);
            }

            // --- HELPER: GET TARGET BOUNDS ---
            (int tLeft, int tTop, int tRight, int tBottom) GetTargetBounds(string targetId)
            {
                int tLeft = int.MaxValue, tTop = int.MaxValue, tRight = int.MinValue, tBottom = int.MinValue;
                foreach (var monitor in monitors)
                {
                    if (monitor.OwnerClientId == targetId)
                    {
                        var gb = monitor.GlobalBounds;
                        tLeft = Math.Min(tLeft, gb.X);
                        tTop = Math.Min(tTop, gb.Y);
                        tRight = Math.Max(tRight, gb.X + gb.Width);
                        tBottom = Math.Max(tBottom, gb.Y + gb.Height);
                    }
                }
                return (tLeft, tTop, tRight, tBottom);
            }

            // --- CORE LOGIC ---
            void ExecuteClaim(OmniMouse.Switching.Direction dir, int globalEntryX, int globalEntryY, int remoteGlobalLeft, int remoteGlobalTop, int remoteWidth, int remoteHeight)
            {
                // 1. SAFETY NUDGE (Global Pixels)
                int nudge = 10;
                int finalGlobalX = globalEntryX;
                int finalGlobalY = globalEntryY;

                switch (dir)
                {
                    case OmniMouse.Switching.Direction.Right: finalGlobalX += nudge; break;
                    case OmniMouse.Switching.Direction.Left: finalGlobalX -= nudge; break;
                    case OmniMouse.Switching.Direction.Down: finalGlobalY += nudge; break;
                    case OmniMouse.Switching.Direction.Up: finalGlobalY -= nudge; break;
                }

                // 2. SYNC LOCAL TRACKER (Global Pixels)
                _remoteCursorX = finalGlobalX;
                _remoteCursorY = finalGlobalY;

                // 3. NORMALIZE FOR NETWORK (Universal 0-65535)
                double relX = finalGlobalX - remoteGlobalLeft;
                double relY = finalGlobalY - remoteGlobalTop;

                const int UniversalMax = 65535;
                int universalX = (int)((relX * UniversalMax) / remoteWidth);
                int universalY = (int)((relY * UniversalMax) / remoteHeight);

                if (universalX < 0) universalX = 0;
                if (universalX > UniversalMax) universalX = UniversalMax;
                if (universalY < 0) universalY = 0;
                if (universalY > UniversalMax) universalY = UniversalMax;

                Console.WriteLine($"[HOOK][EdgeClaim] HIT {dir}. Global:({finalGlobalX},{finalGlobalY}). Relative:({relX},{relY}). SENDING UNIVERSAL:({universalX},{universalY})");

                BeginRemoteStreaming(dir);
                try
                {
                    // Pass the entry direction so Receiver knows which edge to check for returning
                    tx.SendTakeControl(RemotePeerClientId, universalX, universalY, entryDirection: dir);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HOOK][EdgeClaim] SendTakeControl failed: {ex.Message}");
                    EndRemoteStreaming();
                }
            }

            // --- DIRECTION HANDLERS ---
            if (nearRight)
            {
                var (hasLayout, neighborId) = ResolveNeighbor(OmniMouse.Switching.Direction.Right);
                if (hasLayout && string.IsNullOrEmpty(neighborId)) return;
                var targetId = neighborId ?? RemotePeerClientId!;
                SetRemotePeer(targetId);

                var b = GetTargetBounds(targetId);
                ExecuteClaim(OmniMouse.Switching.Direction.Right,
                    b.tLeft, Math.Max(b.tTop, Math.Min(b.tBottom - 1, y)),
                    b.tLeft, b.tTop, b.tRight - b.tLeft, b.tBottom - b.tTop);
            }
            else if (nearLeft)
            {
                var (hasLayout, neighborId) = ResolveNeighbor(OmniMouse.Switching.Direction.Left);
                if (hasLayout && string.IsNullOrEmpty(neighborId)) return;
                var targetId = neighborId ?? RemotePeerClientId!;
                SetRemotePeer(targetId);

                var b = GetTargetBounds(targetId);
                ExecuteClaim(OmniMouse.Switching.Direction.Left,
                    b.tRight - 1, Math.Max(b.tTop, Math.Min(b.tBottom - 1, y)),
                    b.tLeft, b.tTop, b.tRight - b.tLeft, b.tBottom - b.tTop);
            }
            else if (nearTop)
            {
                var (hasLayout, neighborId) = ResolveNeighbor(OmniMouse.Switching.Direction.Up);
                if (hasLayout && string.IsNullOrEmpty(neighborId)) return;
                var targetId = neighborId ?? RemotePeerClientId!;
                SetRemotePeer(targetId);

                var b = GetTargetBounds(targetId);
                ExecuteClaim(OmniMouse.Switching.Direction.Up,
                    Math.Max(b.tLeft, Math.Min(b.tRight - 1, x)), b.tBottom - 1,
                    b.tLeft, b.tTop, b.tRight - b.tLeft, b.tBottom - b.tTop);
            }
            else if (nearBottom)
            {
                var (hasLayout, neighborId) = ResolveNeighbor(OmniMouse.Switching.Direction.Down);
                if (hasLayout && string.IsNullOrEmpty(neighborId)) return;
                var targetId = neighborId ?? RemotePeerClientId!;
                SetRemotePeer(targetId);

                var b = GetTargetBounds(targetId);
                ExecuteClaim(OmniMouse.Switching.Direction.Down,
                    Math.Max(b.tLeft, Math.Min(b.tRight - 1, x)), b.tTop,
                    b.tLeft, b.tTop, b.tRight - b.tLeft, b.tBottom - b.tTop);
            }
        }

        // --- PRE-FLIGHT HELPERS ---
        private static bool SendPreFlightRequest(UdpMouseTransmitter tx)
        {
            try
            {
                var buf = new byte[1];
                buf[0] = MSG_PREFLIGHT_REQUEST;
                tx.SendDirect(buf, buf.Length);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HOOK][PreFlight] Send failed: {ex.Message}");
                return false;
            }
        }

        private static bool WaitForPreFlightAck(int timeoutMs)
        {
            lock (_preFlightLock)
            {
                _preFlightAckReceived = false;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                lock (_preFlightLock)
                {
                    if (_preFlightAckReceived)
                    {
                        _preFlightAckReceived = false;
                        return true;
                    }
                }
                System.Threading.Thread.Sleep(5);
            }

            return false;
        }

        internal static void OnPreFlightAckReceived()
        {
            lock (_preFlightLock)
            {
                _preFlightAckReceived = true;
            }
        }

        // Add public method for Transmitter to call
        internal static void NotifyReceiverEdgeHit()
        {
            _receiverReportedEdgeHit = true;
            Console.WriteLine("[HOOK] Receiver edge hit notification received");
        }

        // Helper: Get Receiver's LOCAL screen bounds (what Receiver sees locally)
        private static (int Left, int Top, int Right, int Bottom)? GetReceiverLocalBounds()
        {
            // This is the Receiver's own local screen bounds
            // NOT the global coordinates where Receiver's monitors are mapped
            // Just the local 0,0 based bounds
            var topology = new OmniMouse.Switching.Win32ScreenTopology();
            var bounds = topology.GetScreenConfiguration();
            return (bounds.DesktopBounds.Left, bounds.DesktopBounds.Top, 
                    bounds.DesktopBounds.Right, bounds.DesktopBounds.Bottom);
        }

        // Helper: Send edge hit notification to Sender
        private static void NotifySenderOfEdgeHit(UdpMouseTransmitter tx)
        {
            try
            {
                var buf = new byte[1];
                buf[0] = MSG_RECEIVER_EDGE_HIT;  // 0x22
                tx.SendDirect(buf, buf.Length);
                Console.WriteLine("[HOOK] Sent edge hit notification to Sender");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HOOK] Failed to notify Sender of edge hit: {ex.Message}");
            }
        }
    }
}