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

                // 2. Calculate TARGET Coordinates (The Fix)
                // We cannot send 'e.UniversalCursorPoint.X' blindly because it reflects the SENDER's position.
                // We must project where we want to land on the TARGET.

                int sendTargetX = e.UniversalCursorPoint.X;
                int sendTargetY = e.UniversalCursorPoint.Y;

                // Note: We use int.MaxValue/MinValue to force the receiver's SetCursorPos 
                // to clamp to the furthest monitor edge, solving the multi-monitor mapping issue.
                switch (e.Direction)
                {
                    case Direction.Left:
                        // Moving Left implies entering the Target's RIGHT edge.
                        // Send a huge number so the Receiver clamps it to its Rightmost Secondary Monitor.
                        sendTargetX = int.MaxValue;
                        break;

                    case Direction.Right:
                        // Moving Right implies entering the Target's LEFT edge.
                        sendTargetX = 0; // Or int.MinValue
                        break;

                    // For Up/Down, we usually want to preserve X and clamp Y, 
                    // but we'll leave that default for now.
                    case Direction.Up:
                        sendTargetY = int.MaxValue; // Enter bottom
                        break;
                    case Direction.Down:
                        sendTargetY = 0; // Enter top
                        break;
                }

                // 3. Send the modified Target Coordinates
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