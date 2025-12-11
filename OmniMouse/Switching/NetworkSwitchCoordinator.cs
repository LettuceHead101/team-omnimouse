using System;
using System.Runtime.InteropServices;
using System.Threading;
using OmniMouse.Hooks;
using OmniMouse.Network;

namespace OmniMouse.Switching
{
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
            this.switcher.SwitchRequested += OnSwitchRequested;
        }

        public void Cleanup()
        {
            if (this.switcher != null)
                this.switcher.SwitchRequested -= OnSwitchRequested;
        }

        private void OnSwitchRequested(object? sender, MachineSwitchEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.ToMachine)) return;

            Console.WriteLine($"[NetworkSwitchCoordinator] Switch: {e.FromMachine} -> {e.ToMachine} (Dir: {e.Direction})");

            try
            {
                var bounds = this.switcher.GetScreenBounds().DesktopBounds;

                // 1. Clamp Local Cursor (Sender Visuals)
                // This keeps the mouse stuck to the edge of the screen briefly while the switch happens
                int localX = e.RawCursorPoint.X;
                int localY = Math.Max(bounds.Top, Math.Min(bounds.Bottom - 1, e.RawCursorPoint.Y));

                switch (e.Direction)
                {
                    case Direction.Left: localX = bounds.Left; break;
                    case Direction.Right: localX = bounds.Right - 1; break;
                    case Direction.Up: localY = bounds.Top; break;
                    case Direction.Down: localY = bounds.Bottom - 1; break;
                }

                // Execute SetCursorPos on background thread to avoid blocking hooks
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        InputHooks.SuppressNextMoveFrom(localX, localY);
                        SetCursorPos(localX, localY);
                    }
                    catch { /* Ignore logging here for speed */ }
                });

                // 2. Calculate target entry coordinates
                // The UniversalCursorPoint preserves the correct position along the non-crossing axis,
                // but we need to adjust the crossing axis to enter at the opposite edge
                int sendTargetX = e.UniversalCursorPoint.X;
                int sendTargetY = e.UniversalCursorPoint.Y;

                const int UniversalMax = 65535;
                const int EdgeOffset = 100; // Small offset from edge in universal coordinates

                switch (e.Direction)
                {
                    case Direction.Right:
                        // Exiting RIGHT → Enter target's LEFT edge (preserve Y)
                        sendTargetX = EdgeOffset;
                        break;

                    case Direction.Left:
                        // Exiting LEFT → Enter target's RIGHT edge (preserve Y)
                        sendTargetX = UniversalMax - EdgeOffset;
                        break;

                    case Direction.Down:
                        // Exiting BOTTOM → Enter target's TOP edge (preserve X)
                        sendTargetY = EdgeOffset;
                        break;
                        
                    case Direction.Up:
                        // Exiting TOP → Enter target's BOTTOM edge (preserve X)
                        sendTargetY = UniversalMax - EdgeOffset;
                        break;
                }

                // 3. Send the corrected target coordinates
                this.transmitter.SendTakeControl(e.ToMachine, sendTargetX, sendTargetY);

                this.switcher.SetActiveMachine(e.ToMachine);
                InputHooks.BeginRemoteStreaming(e.Direction);

                Console.WriteLine($"[NetworkSwitchCoordinator] Sent TakeControl -> {e.ToMachine} @ ({sendTargetX},{sendTargetY})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkSwitchCoordinator] Error: {ex.Message}");
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);
    }
}