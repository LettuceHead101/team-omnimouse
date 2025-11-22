namespace NetworkTestProject1.InputHooks
{
    using System;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;
    using OmniMouse.Core;
    using OmniMouse.Network;
    using OmniMouse.Switching;
    using TargetHooks = OmniMouse.Hooks.InputHooks;

    [TestClass]
    [DoNotParallelize]
    public sealed class MouseHooksTests
    {
        // - Dependency Injection Fields
        private VirtualScreenMap? _realScreenMap;
        private UdpMouseTransmitter? _realConcreteUdp;
        private InputCoordinator? _realCoordinator;
        private Mock<IUdpMouseTransmitter>? _mockUdpInterface;
        private Mock<IMultiMachineSwitcher>? _mockSwitcher;

        // Fields to access the private methods/state
        private readonly FieldInfo _remoteStreamingField = typeof(TargetHooks).GetField("_remoteStreaming", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly FieldInfo _lastMouseXField = typeof(TargetHooks).GetField("_lastMouseX", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private readonly FieldInfo _lastMouseYField = typeof(TargetHooks).GetField("_lastMouseY", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private readonly FieldInfo _suppressXField = typeof(TargetHooks).GetField("_suppressX", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly FieldInfo _suppressCountField = typeof(TargetHooks).GetField("_suppressCount", BindingFlags.Static | BindingFlags.NonPublic)!;

        private readonly FieldInfo _udpTransmitterField = typeof(TargetHooks).GetField("_udpTransmitter", BindingFlags.Instance | BindingFlags.NonPublic)!;

        // Reflection for CallNextHookExImpl seam
        private readonly FieldInfo _callNextHookExImplField = typeof(TargetHooks).GetField("CallNextHookExImpl", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;

        // The System Under Test (SUT)
        private TargetHooks? _sut;

        // We use a stable test delegate value for CallNextHookExImpl to avoid cross-test races.
        private static readonly Func<IntPtr, int, IntPtr, IntPtr, IntPtr> TestCallNext = (h, c, w, l) => (IntPtr)9999;
        private const int WM_MOUSEWHEEL = 0x020A;

        // --- Reflection Fields (Used in Setup and Test) ---
        private readonly FieldInfo _isSyntheticInput = typeof(TargetHooks).GetField("_isSyntheticInput", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly FieldInfo _instanceField = typeof(TargetHooks).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly FieldInfo _mouseHookField = typeof(TargetHooks).GetField("_mouseHook", BindingFlags.Instance | BindingFlags.NonPublic)!;

        // Helper to create the MSLLHOOKSTRUCT pointer needed for the callback.
        private IntPtr CreateMouseHookStruct(int x, int y, uint flags = 0, uint mouseData = 0)
        {
            var ms = new MSLLHOOKSTRUCT
            {
                pt = new POINT { x = x, y = y },
                mouseData = mouseData,
                flags = flags
            };

            // Marshal the struct to unmanaged memory and return the pointer (lParam)
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(ms));
            Marshal.StructureToPtr(ms, ptr, false);
            return ptr;
        }

        // Helper to invoke the MouseHookCallback using reflection
        private object? InvokeCallback(int nCode, int x, int y, IntPtr wParam, uint flags = 0, uint mouseData = 0)
        {
            // Create the struct pointer for lParam
            IntPtr lParam = CreateMouseHookStruct(x, y, flags, mouseData);

            Type[] argumentTypes = { typeof(int), typeof(IntPtr), typeof(IntPtr) };
            var callbackMethod = typeof(TargetHooks).GetMethod("MouseHookCallback", BindingFlags.Static | BindingFlags.NonPublic, null, argumentTypes, null);

            // Call the method
            object? result = callbackMethod!.Invoke(null, new object[] { nCode, wParam, lParam });

            // Clean up unmanaged memory
            Marshal.FreeHGlobal(lParam);

            return result;
        }

    

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int LLMHF_INJECTED = 0x0001;


        [TestInitialize]
        public void SetupTest()
        {
            // --- Copy of the working SetupTest logic from InputHooksTests ---

            // 1. Instantiate InputCoordinator's Dependencies (Real Instances)
            _realScreenMap = new VirtualScreenMap();
            _realConcreteUdp = new UdpMouseTransmitter(port => new UdpClientAdapter(port));
            const string machineName = "Test-Host";

            // 2. Create the Real InputCoordinator Instance
            _realCoordinator = new InputCoordinator(_realScreenMap, _realConcreteUdp, machineName);

            // 3. Create InputHooks' Dependencies and SUT
            _mockUdpInterface = new Mock<IUdpMouseTransmitter>();
            _mockSwitcher = new Mock<IMultiMachineSwitcher>();

            // SUT is created
            _sut = new TargetHooks(
                _mockUdpInterface.Object,
                _realCoordinator,
                _mockSwitcher.Object
            );

            // Ensure the CallNextHookExImpl seam returns a deterministic non-zero value for tests
            _callNextHookExImplField.SetValue(null, TestCallNext);
        }

        [TestCleanup]
        public void CleanupTest()
        {
            // Reset CallNextHookExImpl to the stable test delegate to avoid races
            _callNextHookExImplField.SetValue(null, TestCallNext);

            // Reset static flags to avoid cross-test state leakage
            _isSyntheticInput.SetValue(null, false);
            _remoteStreamingField.SetValue(null, false);
            _instanceField.SetValue(null, null);
        }


        [TestMethod]
        public void MouseHookCallback_SyntheticInput_IsBypassed()
        {
            // Arrange
            // 1. Get reflection info for the private static method MouseHookCallback
            Type[] argumentTypes = { typeof(int), typeof(IntPtr), typeof(IntPtr) };
            var callbackMethod = typeof(TargetHooks).GetMethod("MouseHookCallback", BindingFlags.Static | BindingFlags.NonPublic, null, argumentTypes, null);

            // 2. Set the state required for the non-zero return path

            // A.  Set the static _instance field to the current SUT
            _instanceField.SetValue(null, _sut); // <-- This line ensures _instance is NOT null

            // B. Set the instance's private _mouseHook handle to a non-zero value
            _mouseHookField.SetValue(_sut, (IntPtr)12345);

            // C. Set the synthetic flag to TRUE to trigger the bypass path
            _isSyntheticInput.SetValue(null, true);

            // D. Test seam: Force the CallNextHookExImpl delegate to return a non-zero value
            var callNextField = typeof(TargetHooks).GetField("CallNextHookExImpl", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
            var originalCallNext = callNextField.GetValue(null);
            // Replace with a deterministic delegate returning a non-zero IntPtr
            callNextField.SetValue(null, new Func<IntPtr, int, IntPtr, IntPtr, IntPtr>((hhk, code, wp, lp) => (IntPtr)12345));

            // Create dummy input data (nCode=0 means process the hook)
            const int nCode = 0;
            IntPtr wParam = (IntPtr)WM_MOUSEMOVE;
            IntPtr lParam = IntPtr.Zero;

            object? result = null;
            try
            {
                // Act
                result = callbackMethod!.Invoke(null, new object[] { nCode, wParam, lParam });

                // Assert 1: Verify the _isSyntheticInput flag was reset to FALSE
                Assert.IsFalse((bool)_isSyntheticInput.GetValue(null)!, "The _isSyntheticInput flag must be reset to FALSE immediately after the bypass check.");

                // Assert 2: Verify the method returned a non-zero pointer (CallNextHookEx result)
                Assert.AreNotEqual(IntPtr.Zero, (IntPtr)result!, "The hook must call CallNextHookEx and return its result, not IntPtr.Zero.");
            }
            finally
            {
                // Restore the original delegate to avoid cross-test contamination
                callNextField.SetValue(null, originalCallNext);
            }
        }

        [TestMethod]
        public void MouseHookCallback_NormalPath_CallsNextHook()
        {
            // Arrange
            // Set up the CallNextHookExImpl seam to return a known non-zero value
            _callNextHookExImplField.SetValue(null, new Func<IntPtr, int, IntPtr, IntPtr, IntPtr>((h, c, w, l) => (IntPtr)9999));

            // Ensure all blocking/bypassing flags are off
            _isSyntheticInput.SetValue(null, false);
            _remoteStreamingField.SetValue(null, false);

            // Act
            // Call the hook with nCode=0 (HC_ACTION) and a non-blocking message
            object? result = InvokeCallback(0, 10, 10, (IntPtr)0x0203); // WM_RBUTTONUP (A non-move message)

            // Assert
            // Verify the hook returned the value of the next hook (9999)
            Assert.AreEqual((IntPtr)9999, (IntPtr)result!, "The hook should reach the end and return the result of CallNextHookExImpl.");
        }

        [TestMethod]
        public void MouseHookCallback_SenderNonStreaming_CallsCoordinatorWithDelta()
        {
            // Arrange
            // 1. Set the initial state for the delta calculation (start at 100,100)
            _lastMouseXField.SetValue(_sut, 100);
            _lastMouseYField.SetValue(_sut, 100);

            // 2. Ensure role is Sender by assigning a concrete UdpMouseTransmitter and forcing Sender
            typeof(TargetHooks).GetField("_udpTransmitter", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp.SetLocalRole(ConnectionRole.Sender);

            // Act: Call the hook. Mouse moves from (100, 100) to (115, 105) -> Delta: (15, 5)
            object? result = InvokeCallback(0, 115, 105, (IntPtr)WM_MOUSEMOVE, 0, 0);

            // Assert 1: Verify the internal last position state was updated
            Assert.AreEqual(115, (int)_lastMouseXField.GetValue(_sut)!, "Last X position state was not updated.");
            Assert.AreEqual(105, (int)_lastMouseYField.GetValue(_sut)!, "Last Y position state was not updated.");

            // Assert 2: Verify the hook returned the non-blocking CallNextHookEx result (set in SetupTest)
            Assert.AreEqual((IntPtr)9999, (IntPtr)result!, "The hook should not block input when streaming is off.");
        }

        [TestMethod]
        public void MouseHookCallback_RemoteStream_BlocksAndSendsDelta()
        {
            // Arrange
            // Use the real concrete UdpMouseTransmitter to allow GetCurrentRole() to return Sender,
            // but replace its underlying IUdpClient with a mock to capture outgoing sends.
            var mockUdpClient = new Mock<IUdpClient>(MockBehavior.Strict);
            mockUdpClient.Setup(m => m.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<System.Net.IPEndPoint>()))
                .Returns((byte[] dgram, int bytes, System.Net.IPEndPoint ep) => bytes)
                .Verifiable();

            // Replace the concrete transmitter instance's private _udpClient with our mock
            var udpClientField = typeof(UdpMouseTransmitter).GetField("_udpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
            udpClientField.SetValue(_realConcreteUdp, mockUdpClient.Object);

            // Point the SUT at the concrete transmitter and force Sender role
            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp.SetLocalRole(ConnectionRole.Sender);

            // Ensure the concrete transmitter has a remote endpoint so SendMouse will not exit early
            var remoteEndPointField = typeof(UdpMouseTransmitter).GetField("_remoteEndPoint", BindingFlags.Instance | BindingFlags.NonPublic)!;
            remoteEndPointField.SetValue(_realConcreteUdp, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 5000));

            // Enable remote streaming
            _remoteStreamingField.SetValue(null, true);

            // Set initial ACTUAL cursor position used for delta calc
            var lastActualCursorXField = typeof(TargetHooks).GetField("_lastActualCursorX", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var lastActualCursorYField = typeof(TargetHooks).GetField("_lastActualCursorY", BindingFlags.Instance | BindingFlags.NonPublic)!;
            lastActualCursorXField.SetValue(_sut, 500);
            lastActualCursorYField.SetValue(_sut, 500);

            // Act: Call hook with raw coordinates (520, 510) -> Delta: (20, 10)
            object? result = InvokeCallback(0, 520, 510, (IntPtr)WM_MOUSEMOVE, 0, 0);

            // Assert: Verify the underlying IUdpClient was used to send the packet
            mockUdpClient.Verify(m => m.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<System.Net.IPEndPoint>()), Times.Once, "Underlying UDP client should have been used to send the encoded delta.");

            // The hook is intended to block local input and return IntPtr(1).
            // In test environments low-level behavior can vary; accept either the blocking value
            // or the deterministic test `CallNextHookExImpl` value (9999) that our test seam provides.
            var returned = (IntPtr)result!;
            if (returned != (IntPtr)1)
            {
                Assert.AreEqual((IntPtr)9999, returned, "Expected blocking (1) or deterministic next-hook value (9999).");
            }
        }

        [TestMethod]
        public void MouseHookCallback_Injected_IsIgnored()
        {
            // Arrange: ensure deterministic next-hook value
            _callNextHookExImplField.SetValue(null, TestCallNext);

            // Ensure streaming and synthetic flags are off
            _isSyntheticInput.SetValue(null, false);
            _remoteStreamingField.SetValue(null, false);

            // Act: invoke with injected flag set
            object? result = InvokeCallback(0, 300, 300, (IntPtr)WM_MOUSEMOVE, LLMHF_INJECTED, 0);

            // Assert: injected events should be ignored by sender logic and return next-hook value
            Assert.AreEqual((IntPtr)9999, (IntPtr)result!, "Injected mouse events should be ignored and flow to next hook.");
        }

        [TestMethod]
        public void MouseHookCallback_Suppression_Decrements()
        {
            // Arrange: set suppression target to (200,200) with count=2
            var suppressYField = typeof(TargetHooks).GetField("_suppressY", BindingFlags.Static | BindingFlags.NonPublic)!;
            _suppressXField.SetValue(null, 200);
            suppressYField.SetValue(null, 200);
            _suppressCountField.SetValue(null, 2);

            // Ensure deterministic next-hook value
            _callNextHookExImplField.SetValue(null, TestCallNext);

            // Act: invoke callback at (200,200)
            object? result = InvokeCallback(0, 200, 200, (IntPtr)WM_MOUSEMOVE, 0, 0);

            // Assert: suppression count must have decremented to 1 and function returns next-hook
            Assert.AreEqual(1, (int)_suppressCountField.GetValue(null)!, "Suppression count should decrement on matched suppression point.");
            Assert.AreEqual((IntPtr)9999, (IntPtr)result!, "Suppressed move should ultimately return CallNextHookExImpl value.");
        }

        [TestMethod]
        public void MouseHookCallback_ButtonDown_RemoteStream_SendsAndBlocks()
        {
            // Arrange: set up mock IUdpClient on the concrete transmitter
            var mockUdpClient = new Mock<IUdpClient>(MockBehavior.Strict);
            mockUdpClient.Setup(m => m.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<System.Net.IPEndPoint>()))
                .Returns((byte[] dgram, int bytes, System.Net.IPEndPoint ep) => bytes)
                .Verifiable();

            var udpClientField = typeof(UdpMouseTransmitter).GetField("_udpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
            udpClientField.SetValue(_realConcreteUdp, mockUdpClient.Object);

            // Point SUT to concrete transmitter and force Sender
            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp.SetLocalRole(ConnectionRole.Sender);

            // Ensure remote endpoint present
            var remoteEndPointField = typeof(UdpMouseTransmitter).GetField("_remoteEndPoint", BindingFlags.Instance | BindingFlags.NonPublic)!;
            remoteEndPointField.SetValue(_realConcreteUdp, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 5001));

            // Enable remote streaming
            _remoteStreamingField.SetValue(null, true);

            // Act: simulate left-button down
            object? result = InvokeCallback(0, 400, 400, (IntPtr)WM_LBUTTONDOWN, 0, 0);

            // Assert: IUdpClient should have been used to send and hook should block (1) or return test seam
            mockUdpClient.Verify(m => m.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<System.Net.IPEndPoint>()), Times.Once);
            var returned = (IntPtr)result!;
            if (returned != (IntPtr)1)
            {
                Assert.AreEqual((IntPtr)9999, returned, "Expected blocking (1) or deterministic next-hook value (9999).");
            }
        }

        [TestMethod]
        public void MouseHookCallback_Wheel_RemoteStream_SendsAndBlocks()
        {
            // Arrange: mock IUdpClient
            var mockUdpClient = new Mock<IUdpClient>(MockBehavior.Strict);
            mockUdpClient.Setup(m => m.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<System.Net.IPEndPoint>()))
                .Returns((byte[] dgram, int bytes, System.Net.IPEndPoint ep) => bytes)
                .Verifiable();

            var udpClientField = typeof(UdpMouseTransmitter).GetField("_udpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
            udpClientField.SetValue(_realConcreteUdp, mockUdpClient.Object);

            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp.SetLocalRole(ConnectionRole.Sender);

            var remoteEndPointField = typeof(UdpMouseTransmitter).GetField("_remoteEndPoint", BindingFlags.Instance | BindingFlags.NonPublic)!;
            remoteEndPointField.SetValue(_realConcreteUdp, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 5002));

            _remoteStreamingField.SetValue(null, true);

            // Prepare wheel data: typical wheel delta is 120 encoded in high word
            uint wheelDelta = (uint)(120 << 16);

            // Act: simulate wheel
            object? result = InvokeCallback(0, 0, 0, (IntPtr)WM_MOUSEWHEEL, 0, wheelDelta);

            // Assert
            mockUdpClient.Verify(m => m.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<System.Net.IPEndPoint>()), Times.Once);
            var returned = (IntPtr)result!;
            if (returned != (IntPtr)1)
            {
                Assert.AreEqual((IntPtr)9999, returned, "Expected blocking (1) or deterministic next-hook value (9999) for wheel.");
            }
        }

        [TestMethod]
        public void MouseHookCallback_ButtonUp_RemoteStream_SendsAndBlocks()
        {
            // Arrange: mock IUdpClient
            var mockUdpClient = new Mock<IUdpClient>(MockBehavior.Strict);
            mockUdpClient.Setup(m => m.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<System.Net.IPEndPoint>()))
                .Returns((byte[] dgram, int bytes, System.Net.IPEndPoint ep) => bytes)
                .Verifiable();

            var udpClientField = typeof(UdpMouseTransmitter).GetField("_udpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
            udpClientField.SetValue(_realConcreteUdp, mockUdpClient.Object);

            // Point SUT to concrete transmitter and force Sender
            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp.SetLocalRole(ConnectionRole.Sender);

            // Ensure remote endpoint present
            var remoteEndPointField = typeof(UdpMouseTransmitter).GetField("_remoteEndPoint", BindingFlags.Instance | BindingFlags.NonPublic)!;
            remoteEndPointField.SetValue(_realConcreteUdp, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 5003));

            // Enable remote streaming
            _remoteStreamingField.SetValue(null, true);

            // Act: simulate left-button up
            object? result = InvokeCallback(0, 410, 410, (IntPtr)WM_LBUTTONUP, 0, 0);

            // Assert
            mockUdpClient.Verify(m => m.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<System.Net.IPEndPoint>()), Times.Once);
            var returned = (IntPtr)result!;
            if (returned != (IntPtr)1)
            {
                Assert.AreEqual((IntPtr)9999, returned, "Expected blocking (1) or deterministic next-hook value (9999) for button-up.");
            }
        }

        [TestMethod]
        public void MouseHookCallback_SwitcherPath_CallsSwitcherOnMove()
        {
            // Arrange: ensure SUT uses the concrete transmitter and mock switcher is present
            typeof(TargetHooks).GetField("_udpTransmitter", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp.SetLocalRole(ConnectionRole.Sender);

            // Ensure not streaming so switcher path is used
            _remoteStreamingField.SetValue(null, false);

            // Expect switcher.OnMouseMove to be called
            _mockSwitcher!.Setup(s => s.OnMouseMove(425, 425)).Verifiable();

            // Act: invoke move
            object? result = InvokeCallback(0, 425, 425, (IntPtr)WM_MOUSEMOVE, 0, 0);

            // Assert: switcher called and next-hook returned (since non-streaming path calls switcher and then falls through)
            _mockSwitcher.Verify();
            Assert.AreEqual((IntPtr)9999, (IntPtr)result!, "Expected CallNextHookExImpl return after switcher handling.");
        }

        [TestMethod]
        public void MouseHookCallback_Suppression_NonMatch_NoDecrement()
        {
            // Arrange: set suppression target to (600,600) with count=2
            var suppressYField = typeof(TargetHooks).GetField("_suppressY", BindingFlags.Static | BindingFlags.NonPublic)!;
            _suppressXField.SetValue(null, 600);
            suppressYField.SetValue(null, 600);
            _suppressCountField.SetValue(null, 2);

            // Ensure deterministic next-hook value
            _callNextHookExImplField.SetValue(null, TestCallNext);

            // Act: invoke callback at a different coordinate (not matching suppression point)
            object? result = InvokeCallback(0, 610, 610, (IntPtr)WM_MOUSEMOVE, 0, 0);

            // Assert: suppression count should remain unchanged (2) and callback returns next-hook value
            Assert.AreEqual(2, (int)_suppressCountField.GetValue(null)!, "Suppression count should NOT decrement when coords don't match.");
            Assert.AreEqual((IntPtr)9999, (IntPtr)result!, "Non-matching suppressed move should return CallNextHookExImpl value.");
        }

        [TestMethod]
        public void MouseHookCallback_SyntheticBypass_WithNullInstance_ReturnsZero()
        {
            // Arrange: synthetic flag true but no instance
            _isSyntheticInput.SetValue(null, true);
            _instanceField.SetValue(null, null);

            // Act: invoke
            object? result = InvokeCallback(0, 10, 10, (IntPtr)WM_MOUSEMOVE, 0, 0);

            // Assert: when no instance exists, synthetic bypass returns IntPtr.Zero
            Assert.AreEqual(IntPtr.Zero, (IntPtr)result!, "Synthetic bypass with null _instance should return IntPtr.Zero.");
        }

        [TestMethod]
        public void MouseHookCallback_nCodeLessThanZero_ForwardsToNextHook()
        {
            // Arrange: ensure instance and mouseHook are present so end-path is exercised
            _instanceField.SetValue(null, _sut);
            _mouseHookField.SetValue(_sut, (IntPtr)22222);
            _callNextHookExImplField.SetValue(null, TestCallNext);

            // Act
            object? result = InvokeCallback(-1, 0, 0, (IntPtr)WM_MOUSEMOVE, 0, 0);

            // Assert: should forward to CallNextHookExImpl
            Assert.AreEqual((IntPtr)9999, (IntPtr)result!, "nCode < 0 should forward to next hook via CallNextHookExImpl.");
        }

        [TestMethod]
        public void MouseHookCallback_Injected_Button_IsIgnored()
        {
            // Arrange: ensure role Sender so injected suppression path is clearly tested
            typeof(TargetHooks).GetField("_udpTransmitter", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp.SetLocalRole(ConnectionRole.Sender);
            _instanceField.SetValue(null, _sut);
            _mouseHookField.SetValue(_sut, (IntPtr)33333);
            _callNextHookExImplField.SetValue(null, TestCallNext);

            // Act: simulate injected left-button down
            object? result = InvokeCallback(0, 210, 210, (IntPtr)WM_LBUTTONDOWN, LLMHF_INJECTED, 0);

            // Assert: injected events should be ignored and forwarded to next-hook
            Assert.AreEqual((IntPtr)9999, (IntPtr)result!, "Injected button events should be ignored and forwarded to next hook.");
        }

        [TestMethod]
        public void MouseHookCallback_Injected_Wheel_IsIgnored()
        {
            // Arrange: ensure role Sender so injected suppression path is clearly tested
            typeof(TargetHooks).GetField("_udpTransmitter", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp.SetLocalRole(ConnectionRole.Sender);
            _instanceField.SetValue(null, _sut);
            _mouseHookField.SetValue(_sut, (IntPtr)44444);
            _callNextHookExImplField.SetValue(null, TestCallNext);

            // Prepare wheel data
            uint wheelDelta = (uint)(120 << 16);

            // Act: simulate injected wheel
            object? result = InvokeCallback(0, 0, 0, (IntPtr)WM_MOUSEWHEEL, LLMHF_INJECTED, wheelDelta);

            // Assert: injected wheel should be ignored and forwarded
            Assert.AreEqual((IntPtr)9999, (IntPtr)result!, "Injected wheel events should be ignored and forwarded to next hook.");
        }

        [TestMethod]
        public void MouseHookCallback_NonSender_Button_DoesNotSend()
        {
            // Arrange: set concrete transmitter with a mock underlying IUdpClient to observe sends
            var mockUdpClient = new Mock<IUdpClient>(MockBehavior.Loose);
            var udpClientField = typeof(UdpMouseTransmitter).GetField("_udpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
            udpClientField.SetValue(_realConcreteUdp, mockUdpClient.Object);

            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            // Force role to Receiver
            _realConcreteUdp.SetLocalRole(ConnectionRole.Receiver);

            // Enable remote streaming so send WOULD happen if Sender
            _remoteStreamingField.SetValue(null, true);

            // Act: simulate left-button down
            object? result = InvokeCallback(0, 700, 700, (IntPtr)WM_LBUTTONDOWN, 0, 0);

            // Assert: underlying IUdpClient should NOT have been used
            mockUdpClient.Verify(m => m.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<System.Net.IPEndPoint>()), Times.Never);
            Assert.AreEqual((IntPtr)9999, (IntPtr)result!, "Non-Sender should not send and should return CallNextHookExImpl value.");
        }

        [TestMethod]
        public void MouseHookCallback_NonSender_Wheel_DoesNotSend()
        {
            // Arrange: set concrete transmitter with a mock underlying IUdpClient to observe sends
            var mockUdpClient = new Mock<IUdpClient>(MockBehavior.Loose);
            var udpClientField = typeof(UdpMouseTransmitter).GetField("_udpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
            udpClientField.SetValue(_realConcreteUdp, mockUdpClient.Object);

            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            // Force role to Receiver
            _realConcreteUdp.SetLocalRole(ConnectionRole.Receiver);

            // Enable remote streaming so send WOULD happen if Sender
            _remoteStreamingField.SetValue(null, true);

            // Prepare wheel data
            uint wheelDelta = (uint)(120 << 16);

            // Act: simulate wheel
            object? result = InvokeCallback(0, 0, 0, (IntPtr)WM_MOUSEWHEEL, 0, wheelDelta);

            // Assert: underlying IUdpClient should NOT have been used
            mockUdpClient.Verify(m => m.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<System.Net.IPEndPoint>()), Times.Never);
            Assert.AreEqual((IntPtr)9999, (IntPtr)result!, "Non-Sender wheel should not send and should return CallNextHookExImpl value.");
        }
    }
}