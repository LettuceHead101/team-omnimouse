using System;
using System.Runtime.InteropServices;
using OmniMouse.Hooks;

namespace OmniMouse.Network
{
    /// <summary>
    /// Coordinates local input with the VirtualScreenMap and the network transmitter.
    /// Purely responsible for detecting monitor ownership transitions and issuing a
    /// precise "take control at (x,y)" message to the target client.
    /// Caller/higher layers own starting/stopping hooks and wiring events.
    /// </summary>
    public sealed class InputCoordinator
    {
        private readonly VirtualScreenMap _screenMap;
        private readonly UdpMouseTransmitter _udpService;
        private readonly string _selfClientId;

        // Protects _globalMouseX/_globalMouseY/_currentActiveClientId/_lastMonitor
        private readonly object _coordinatorLock = new object();

        // The ID of the client that currently has control. Starts as this machine.
        private string _currentActiveClientId;

        // Current global virtual cursor position (integer coordinates)
        private int _globalMouseX;
        private int _globalMouseY;

        // Optional: last known monitor (helps dead-zone clamping)
        private MonitorInfo? _lastMonitor;

        // Event to notify caller that this machine should become Server and start hooks.
        public event Action? BecameServer;

        public InputCoordinator(VirtualScreenMap map, UdpMouseTransmitter udpService, string selfClientId)
        {
            _screenMap = map ?? throw new ArgumentNullException(nameof(map));
            _udpService = udpService ?? throw new ArgumentNullException(nameof(udpService));
            _selfClientId = selfClientId ?? throw new ArgumentNullException(nameof(selfClientId));
            _currentActiveClientId = _selfClientId;

            // initialize global position to current cursor position (desktop coords)
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

            // Subscribe to incoming take-control messages so we can become server
            _udpService.TakeControlReceived += OnReceiveTakeControl;
        }

        // Called by the low-level hook on the active server with motion DELTAS.
        // IMPORTANT: pass deltas, not absolute moves.
        public void OnMouseInput(int deltaX, int deltaY)
        {
            string? targetClientId = null;
            int sendLocalX = 0, sendLocalY = 0;

            lock (_coordinatorLock)
            {
                _globalMouseX += deltaX;
                _globalMouseY += deltaY;

                if (_screenMap.TranslateGlobalToLocal(_globalMouseX, _globalMouseY, out var monitor, out var localX, out var localY))
                {
                    // Valid monitor under the virtual map
                    _lastMonitor = monitor;

                    if (monitor != null && monitor.OwnerClientId != _currentActiveClientId)
                    {
                        Console.WriteLine($"[InputCoordinator] Transition from {_currentActiveClientId} -> {monitor.OwnerClientId} at global({_globalMouseX},{_globalMouseY}) -> local({localX},{localY})");

                        targetClientId = monitor.OwnerClientId;
                        sendLocalX = localX;
                        sendLocalY = localY;

                        // Update our local state: we have relinquished control.
                        _currentActiveClientId = monitor.OwnerClientId;
                        // The UdpMouseTransmitter.SendTakeControl already calls SetLocalRole(Receiver).
                    }
                }
                else
                {
                    // Simple policy for dead zones: clamp to last known monitor boundary to avoid losing the cursor.
                    if (_lastMonitor != null)
                    {
                        var gb = _lastMonitor.GlobalBounds;
                        _globalMouseX = Math.Clamp(_globalMouseX, gb.Left, gb.Right - 1);
                        _globalMouseY = Math.Clamp(_globalMouseY, gb.Top, gb.Bottom - 1);
                    }
                    else
                    {
                        // No map context: clamp to non-negative
                        _globalMouseX = Math.Max(0, _globalMouseX);
                        _globalMouseY = Math.Max(0, _globalMouseY);
                    }
                }
            }

            // Send outside lock to avoid holding the lock during I/O
            if (targetClientId != null)
            {
                _udpService.SendTakeControl(targetClientId, sendLocalX, sendLocalY);
            }
        }

        // When a take-control packet arrives for this machine, become the server.
        private void OnReceiveTakeControl(int localX, int localY)
        {
            Console.WriteLine($"[InputCoordinator] Received TakeControl -> become server and set cursor to ({localX},{localY})");
            
            // End remote streaming mode since we're now receiving control
            InputHooks.EndRemoteStreaming();
            
            // IMPORTANT: Remain Receiver so this machine will apply incoming mouse moves
            // from the peer. The sender continues streaming motion events after take-control.
            _udpService.SetLocalRole(ConnectionRole.Receiver);

            // Position cursor exactly at the requested local coords
            SetCursorPos(localX, localY);

            // Update internal global mouse position according to the virtual map if possible
            lock (_coordinatorLock)
            {
                // Try to translate this monitor/local into global coordinates:
                // Find monitor containing (localX, localY) that belongs to this client.
                foreach (var m in _screenMap.GetMonitorsSnapshot())
                {
                    if (m.OwnerClientId == _selfClientId)
                    {
                        // local-space to global-space mapping if this local coordinate falls within the local bounds
                        if (localX >= m.LocalBounds.Left && localX < m.LocalBounds.Right && localY >= m.LocalBounds.Top && localY < m.LocalBounds.Bottom)
                        {
                            _globalMouseX = m.GlobalBounds.Left + (localX - m.LocalBounds.Left);
                            _globalMouseY = m.GlobalBounds.Top + (localY - m.LocalBounds.Top);
                            _lastMonitor = m;
                            break;
                        }
                    }
                }

                _currentActiveClientId = _selfClientId;
            }

            // We intentionally do not start hooks here; the sender keeps streaming.
        }

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }
    }
}