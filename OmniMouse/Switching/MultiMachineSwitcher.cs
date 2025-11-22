using System;
using System.Drawing;

namespace OmniMouse.Switching
{
    /// <summary>
    /// Main implementation of multi-machine switching logic.
    /// Orchestrates screen detection, edge evaluation, and switch coordination.
    /// </summary>
    public class MultiMachineSwitcher : IMultiMachineSwitcher
    {
        private readonly IScreenTopology _topology;
        private readonly IMachineLayout _layout;
        private readonly ISwitchPolicy _policy;
        private readonly ICoordinateMapper _mapper;
        
        private ScreenBounds? _currentBounds;
        private bool _isRunning;
        private string _activeMachine = string.Empty;
        private bool _isController = true; // Whether this machine is the controller

        public event EventHandler<MachineSwitchEventArgs>? SwitchRequested;

        public MultiMachineSwitcher(
            IScreenTopology topology,
            IMachineLayout layout,
            ISwitchPolicy policy,
            ICoordinateMapper mapper)
        {
            _topology = topology ?? throw new ArgumentNullException(nameof(topology));
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public void Start()
        {
            if (_isRunning)
                return;

            Console.WriteLine("[MultiMachineSwitcher] Starting...");
            
            // Refresh screen configuration
            _currentBounds = _topology.GetScreenConfiguration();
            
            Console.WriteLine($"[MultiMachineSwitcher] Screen config: Desktop={_currentBounds.DesktopBounds}, Primary={_currentBounds.PrimaryScreenBounds}");
            Console.WriteLine($"[MultiMachineSwitcher] Sensitive points: {_currentBounds.SensitivePoints.Length}");

            _isRunning = true;
        }

        public void Stop()
        {
            if (!_isRunning)
                return;

            Console.WriteLine("[MultiMachineSwitcher] Stopping...");
            _isRunning = false;
        }

        public void UpdateMatrix(string[] machines, bool oneRow = true, bool wrapAround = false)
        {
            if (machines == null || machines.Length == 0)
                throw new ArgumentException("Machine list cannot be empty", nameof(machines));

            Console.WriteLine($"[MultiMachineSwitcher] Updating matrix: {string.Join(", ", machines)} (OneRow={oneRow}, Wrap={wrapAround})");

            if (_layout is DefaultMachineLayout defaultLayout)
            {
                // Update existing layout
                for (int i = 0; i < Math.Min(machines.Length, 4); i++)
                {
                    defaultLayout.Machines[i] = machines[i] ?? string.Empty;
                }
                defaultLayout.IsOneRow = oneRow;
                defaultLayout.EnableWrapAround = wrapAround;
            }

            _layout.CurrentMachine = _activeMachine;
        }

        public void SetActiveMachine(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Machine name cannot be empty", nameof(name));

            Console.WriteLine($"[MultiMachineSwitcher] Setting active machine: {name}");
            _activeMachine = name;
            _layout.CurrentMachine = name;
        }

        public void OnMouseMove(int x, int y)
        {
            if (!_isRunning)
                return;

            if (_currentBounds == null)
                return;

            // Build evaluation context
            var context = new MouseMoveContext
            {
                Timestamp = DateTime.Now,
                RawPixel = new Point(x, y),
                DesktopBounds = _currentBounds.DesktopBounds,
                PrimaryBounds = _currentBounds.PrimaryScreenBounds,
                CurrentMachine = _activeMachine,
                IsController = _isController,
                SensitivePoints = _currentBounds.SensitivePoints
            };

            // Evaluate switch policy
            var decision = _policy.Evaluate(context);

            if (decision.ShouldSwitch && !string.IsNullOrWhiteSpace(decision.TargetMachine))
            {
                Console.WriteLine($"[MultiMachineSwitcher] Switch decision: {_activeMachine} -> {decision.TargetMachine} (Reason: {decision.Reason})");

                // Raise event for consumers
                var args = new MachineSwitchEventArgs(
                    _activeMachine,
                    decision.TargetMachine,
                    context.RawPixel,
                    decision.UniversalPoint,
                    decision.Reason,
                    decision.Direction);

                SwitchRequested?.Invoke(this, args);
            }
        }

        public ScreenBounds GetScreenBounds()
        {
            if (_currentBounds == null)
                _currentBounds = _topology.GetScreenConfiguration();

            return _currentBounds;
        }

        /// <summary>
        /// Set whether this machine is the controller (affects coordinate mapping).
        /// </summary>
        public void SetIsController(bool isController)
        {
            _isController = isController;
        }

        /// <summary>
        /// refresh screen configuration (call when display settings change).
        /// </summary>
        public void RefreshScreenConfiguration()
        {
            Console.WriteLine("[MultiMachineSwitcher] Refreshing screen configuration...");
            _currentBounds = _topology.GetScreenConfiguration();
        }
    }
}
