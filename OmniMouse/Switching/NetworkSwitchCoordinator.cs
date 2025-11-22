using System;
using System.Runtime.InteropServices;
using System.Threading;
using OmniMouse.Hooks;
using OmniMouse.Network;

namespace OmniMouse.Switching
{
    /// <summary>
    /// Bridges the MultiMachineSwitcher with the network layer (UdpMouseTransmitter).
    /// hhandles switch events and coordinates network communication.
    /// </summary>
    public class NetworkSwitchCoordinator
    {
        private readonly IMultiMachineSwitcher switcher;
        private readonly IUdpMouseTransmitter transmitter;
        private readonly string localMachineName;

        public NetworkSwitchCoordinator(
            IMultiMachineSwitcher switcher,
            IUdpMouseTransmitter transmitter,
            string localMachineName)
        {
            this.switcher = switcher ?? throw new ArgumentNullException(nameof(switcher));
            this.transmitter = transmitter ?? throw new ArgumentNullException(nameof(transmitter));
            this.localMachineName = localMachineName ?? throw new ArgumentNullException(nameof(localMachineName));

            // subscribe to switch events
            this.switcher.SwitchRequested += OnSwitchRequested;
        }

        public void Cleanup()
        {
            if (this.switcher != null)
            {
                this.switcher.SwitchRequested -= OnSwitchRequested;
            }
        }

        private void OnSwitchRequested(object? sender, MachineSwitchEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.ToMachine))
            {
                return;
            }

            Console.WriteLine($"[NetworkSwitchCoordinator] Switch: {e.FromMachine} -> {e.ToMachine}");
            Console.WriteLine($"  Reason: {e.Reason}, Direction: {e.Direction}");
            Console.WriteLine($"  Universal Point: ({e.UniversalCursorPoint.X}, {e.UniversalCursorPoint.Y})");

            try
            {
                // clamp the local cursor to the touched edge to avoid oscillation/retrigger
                // CRITICAL: SetCursorPos MUST be called asynchronously to avoid blocking the mouse hook thread
                try
                {
                    var bounds = this.switcher.GetScreenBounds().DesktopBounds;
                    int x = e.RawCursorPoint.X;
                    int y = e.RawCursorPoint.Y;

                    // Clamp Y within desktop
                    y = Math.Max(bounds.Top, Math.Min(bounds.Bottom - 1, y));

                    switch (e.Direction)
                    {
                        case Direction.Left:
                            x = bounds.Left;
                            break;
                        case Direction.Right:
                            x = bounds.Right - 1;
                            break;
                        case Direction.Up:
                            x = Math.Max(bounds.Left, Math.Min(bounds.Right - 1, x));
                            y = bounds.Top;
                            break;
                        case Direction.Down:
                            x = Math.Max(bounds.Left, Math.Min(bounds.Right - 1, x));
                            y = bounds.Bottom - 1;
                            break;
                    }

                    // Capture values for async callback
                    int clampedX = x;
                    int clampedY = y;

                    // CRITICAL FIX: Execute SetCursorPos on a background thread to prevent UI freeze
                    // Calling SetCursorPos directly in the mouse hook callback causes re-entrant blocking
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            // Suppress feedback for the position we are about to set
                            InputHooks.SuppressNextMoveFrom(clampedX, clampedY);
                            SetCursorPos(clampedX, clampedY);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[NetworkSwitchCoordinator] SetCursorPos failed: {ex.Message}");
                        }
                    });
                }
                catch (Exception clampEx)
                {
                    Console.WriteLine($"[NetworkSwitchCoordinator] Clamp failed: {clampEx.Message}");
                }

                // Send take-control message to target machine
                // IMPORTANT: Send the UNIVERSAL (0..65535) coordinates so the receiver can
                // map to its own screen bounds and start at its LEFT/RIGHT edge appropriately.
                this.transmitter.SendTakeControl(
                    e.ToMachine,
                    e.UniversalCursorPoint.X,
                    e.UniversalCursorPoint.Y);

                // Update active machine in switcher
                this.switcher.SetActiveMachine(e.ToMachine);

                // Enable continuous remote streaming of local mouse moves until we switch back
                InputHooks.BeginRemoteStreaming();

                Console.WriteLine($"[NetworkSwitchCoordinator] Sent take-control to {e.ToMachine}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkSwitchCoordinator] Error sending switch: {ex.Message}");
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);
    }
}
