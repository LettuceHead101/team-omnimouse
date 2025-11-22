using System;
using System.Windows;
using OmniMouse.Hooks;
using OmniMouse.Network;
using OmniMouse.Switching;

namespace OmniMouse
{
    /// <summary>
    /// Add this to your App.xaml.cs or MainWindow initialization.
    /// </summary>
    public class SwitchingIntegration
    {
        private IMultiMachineSwitcher? _switcher;
        private NetworkSwitchCoordinator? _coordinator;
        private InputHooks? _hooks;

        public void Initialize(
            string localMachineName,
            string[] machineNames,
            IUdpMouseTransmitter transmitter,
            InputCoordinator inputCoordinator)
        {
            try
            {
                Console.WriteLine("[SwitchingIntegration] Initializing switching...");

                // 1. Create screen topology detector
                var topology = new Win32ScreenTopology();

                // 2. Create machine layout (2x2 grid with wraparound)
                var layout = new DefaultMachineLayout(machineNames, oneRow: false, wrapAround: true);
                layout.CurrentMachine = localMachineName;

                // 3. Create coordinate mapper
                var mapper = new DefaultCoordinateMapper();

                // 4. Create switch policy
                var policy = new DefaultSwitchPolicy(layout, mapper)
                {
                    EdgeThresholdPixels = 2,
                    CooldownMilliseconds = 100,
                    BlockAtCorners = false,
                    UseRelativeMovement = true
                };

                // 5. Create the main switcher
                _switcher = new MultiMachineSwitcher(topology, layout, policy, mapper);
                _switcher.SetActiveMachine(localMachineName);
                _switcher.UpdateMatrix(machineNames, oneRow: false, wrapAround: true);

                // 6. Create network coordinator to handle switch events
                _coordinator = new NetworkSwitchCoordinator(_switcher, transmitter, localMachineName);

                // 7. Start the switcher
                _switcher.Start();

                // 8. Wire up to input hooks (if using new switcher instead of InputCoordinator)
                // Note: You can pass _switcher to InputHooks constructor
                // _hooks = new InputHooks(transmitter, inputCoordinator, _switcher);
                // _hooks.InstallHooks();

                Console.WriteLine("[SwitchingIntegration] Initialization complete!");
                Console.WriteLine($"  Active machine: {localMachineName}");
                Console.WriteLine($"  Machine matrix: {string.Join(", ", machineNames)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SwitchingIntegration] Initialization failed: {ex}");
                MessageBox.Show($"Failed to initialize switching: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void Shutdown()
        {
            Console.WriteLine("[SwitchingIntegration] Shutting down...");
            
            _switcher?.Stop();
            _coordinator?.Cleanup();
            _hooks?.UninstallHooks();
        }

        public IMultiMachineSwitcher? Switcher => _switcher;
    }
}
