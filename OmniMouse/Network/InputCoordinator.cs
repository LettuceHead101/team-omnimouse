using System;
using System.Runtime.InteropServices;
using OmniMouse.Hooks;
using System.Linq;
using System.Collections.Generic;

namespace OmniMouse.Network
{
    public sealed class InputCoordinator
    {
        private readonly VirtualScreenMap _screenMap;
        private readonly UdpMouseTransmitter _udpService;
        private readonly string _selfClientId;

        internal VirtualScreenMap ScreenMap => _screenMap;
        internal string SelfClientId => _selfClientId;

        private readonly object _coordinatorLock = new object();
        private string _currentActiveClientId;

        private int _globalMouseX;
        private int _globalMouseY;
        private MonitorInfo? _lastMonitor;

        public event Action? BecameServer;

        public InputCoordinator(VirtualScreenMap map, UdpMouseTransmitter udpService, string selfClientId)
        {
            _screenMap = map ?? throw new ArgumentNullException(nameof(map));
            _udpService = udpService ?? throw new ArgumentNullException(nameof(udpService));
            _selfClientId = selfClientId ?? throw new ArgumentNullException(nameof(selfClientId));
            _currentActiveClientId = _selfClientId;

            if (GetCursorPos(out var p))
            {
                _globalMouseX = p.x;
                _globalMouseY = p.y;
            }
            else
            {
                _globalMouseX = 0;
                _globalMouseY = 0;
            }

            _udpService.TakeControlReceived += OnReceiveTakeControl;
        }

        public void OnMouseInput(int deltaX, int deltaY)
        {
            string? targetClientId = null;
            int sendLocalX = 0, sendLocalY = 0;

            lock (_coordinatorLock)
            {
                // 1. Apply Deltas
                int appliedX = deltaX;
                int appliedY = deltaY;

                // (Keeping existing clipping logic for brevity - it is correct)
                if (_lastMonitor != null)
                {
                    var gb = _lastMonitor.GlobalBounds;
                    if (deltaX > 0 && _screenMap.FindNeighbor(_lastMonitor, "right") == null)
                    {
                        int maxForward = (gb.Right - 1) - _globalMouseX;
                        if (maxForward <= 0) appliedX = 0;
                        else if (deltaX > maxForward) appliedX = maxForward;
                    }
                    else if (deltaX < 0 && _screenMap.FindNeighbor(_lastMonitor, "left") == null)
                    {
                        int maxBack = _globalMouseX - gb.Left;
                        if (maxBack <= 0) appliedX = 0;
                        else if (-deltaX > maxBack) appliedX = -maxBack;
                    }
                    if (deltaY > 0 && _screenMap.FindNeighbor(_lastMonitor, "down") == null)
                    {
                        int maxDown = (gb.Bottom - 1) - _globalMouseY;
                        if (maxDown <= 0) appliedY = 0;
                        else if (deltaY > maxDown) appliedY = maxDown;
                    }
                    else if (deltaY < 0 && _screenMap.FindNeighbor(_lastMonitor, "up") == null)
                    {
                        int maxUp = _globalMouseY - gb.Top;
                        if (maxUp <= 0) appliedY = 0;
                        else if (-deltaY > maxUp) appliedY = -maxUp;
                    }
                }

                _globalMouseX += appliedX;
                _globalMouseY += appliedY;

                // Clamp to non-negative if no monitor/lastMonitor exists
                if (_lastMonitor == null)
                {
                    if (_globalMouseX < 0) _globalMouseX = 0;
                    if (_globalMouseY < 0) _globalMouseY = 0;
                }

                // 2. Check Monitor & Transitions
                if (_screenMap.TranslateGlobalToLocal(_globalMouseX, _globalMouseY, out var monitor, out var _, out var _))
                {
                    _lastMonitor = monitor;

                    // Transition Detected
                    if (monitor != null && monitor.OwnerClientId != _currentActiveClientId)
                    {
                        Console.WriteLine($"[InputCoordinator] Transition {_currentActiveClientId} -> {monitor.OwnerClientId}");

                        targetClientId = monitor.OwnerClientId;
                        _currentActiveClientId = monitor.OwnerClientId;

                        // --- EDGE SNAP LOGIC ---
                        // Force cursor to the correct edge of the Target PC
                        var targetMonitors = _screenMap.GetMonitorsSnapshot()
                                                       .Where(m => m.OwnerClientId == targetClientId)
                                                       .ToList();

                        if (targetMonitors.Count > 0)
                        {
                            MonitorInfo? edgeMonitor = null;

                            if (deltaX < 0) // Moving Left (Entering from Right)
                            {
                                // Find rightmost monitor(s)
                                edgeMonitor = targetMonitors.OrderByDescending(m => m.GlobalBounds.Right).FirstOrDefault();

                                if (edgeMonitor != null)
                                {
                                    // Snap Global X to the Right Edge of that monitor
                                    int relativeX = edgeMonitor.GlobalBounds.Right - 1;

                                    // Preserve Y relative to center if needed, or simple clamp
                                    int clampedY = Math.Max(edgeMonitor.GlobalBounds.Top, Math.Min(_globalMouseY, edgeMonitor.GlobalBounds.Bottom - 1));

                                    _globalMouseX = relativeX;
                                    _globalMouseY = clampedY;
                                    _lastMonitor = edgeMonitor;
                                }
                            }
                            else if (deltaX > 0) // Moving Right (Entering from Left)
                            {
                                edgeMonitor = targetMonitors.OrderBy(m => m.GlobalBounds.Left).FirstOrDefault();

                                if (edgeMonitor != null)
                                {
                                    int relativeX = edgeMonitor.GlobalBounds.Left;
                                    int clampedY = Math.Max(edgeMonitor.GlobalBounds.Top, Math.Min(_globalMouseY, edgeMonitor.GlobalBounds.Bottom - 1));

                                    _globalMouseX = relativeX;
                                    _globalMouseY = clampedY;
                                    _lastMonitor = edgeMonitor;
                                }
                            }
                        }
                    }
                }

                // 3. Coordinate Translation (THE FIX)
                // Instead of trusting LocalBounds of the specific monitor, we calculate position relative to the Target Client's origin.
                if (_lastMonitor != null)
                {
                    string targetOwner = _lastMonitor.OwnerClientId;

                    // Find the "Origin" monitor for this client (Top-Left most monitor)
                    var clientMonitors = _screenMap.GetMonitorsSnapshot().Where(m => m.OwnerClientId == targetOwner).ToList();

                    if (clientMonitors.Count > 0)
                    {
                        // Calculate Client's Global Origin (Min Global X, Min Global Y)
                        int clientMinGlobalX = clientMonitors.Min(m => m.GlobalBounds.Left);
                        int clientMinGlobalY = clientMonitors.Min(m => m.GlobalBounds.Top);

                        // Calculate Client's Local Origin (Min Local X, Min Local Y) - Usually (0,0)
                        int clientMinLocalX = clientMonitors.Min(m => m.LocalBounds.Left);
                        int clientMinLocalY = clientMonitors.Min(m => m.LocalBounds.Top);

                        // Map Global Mouse to Client Local Space
                        // Formula: (CurrentGlobal - ClientGlobalStart) + ClientLocalStart
                        sendLocalX = (_globalMouseX - clientMinGlobalX) + clientMinLocalX;
                        sendLocalY = (_globalMouseY - clientMinGlobalY) + clientMinLocalY;

                        // Debug Log to verify coordinates
                        if (targetClientId != null)
                        {
                            Console.WriteLine($"[InputCoordinator] Sending: Global({_globalMouseX}) -> Local({sendLocalX}). Target Origin GlobalX: {clientMinGlobalX}");
                        }
                    }
                    else
                    {
                        // Fallback
                        sendLocalX = _globalMouseX;
                        sendLocalY = _globalMouseY;
                    }
                }
            }

            if (targetClientId != null)
            {
                _udpService.SendTakeControl(targetClientId, sendLocalX, sendLocalY);
            }
        }

        private void OnReceiveTakeControl(int localX, int localY)
        {
            Console.WriteLine($"[InputCoordinator] Received TakeControl -> SetCursorPos({localX},{localY})");
            InputHooks.EndRemoteStreaming();
            _udpService.SetLocalRole(ConnectionRole.Receiver);
            SetCursorPos(localX, localY);

            // Sync internal state to match the received position
            lock (_coordinatorLock)
            {
                _currentActiveClientId = _selfClientId;
                // Re-find the monitor that contains this local point to sync _globalMouseX
                foreach (var m in _screenMap.GetMonitorsSnapshot().Where(x => x.OwnerClientId == _selfClientId))
                {
                    // Check if localX/Y is within this monitor's projected local bounds
                    // We assume LocalBounds are correct desktop coords here.
                    if (localX >= m.LocalBounds.Left && localX < m.LocalBounds.Right &&
                        localY >= m.LocalBounds.Top && localY < m.LocalBounds.Bottom)
                    {
                        _globalMouseX = m.GlobalBounds.Left + (localX - m.LocalBounds.Left);
                        _globalMouseY = m.GlobalBounds.Top + (localY - m.LocalBounds.Top);
                        _lastMonitor = m;
                        break;
                    }
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }
    }
}