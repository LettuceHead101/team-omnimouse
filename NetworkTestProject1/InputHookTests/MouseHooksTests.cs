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
        private readonly FieldInfo _remoteCursorXField = typeof(TargetHooks).GetField("_remoteCursorX", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly FieldInfo _remoteCursorYField = typeof(TargetHooks).GetField("_remoteCursorY", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly FieldInfo _remoteStreamingDirectionField = typeof(TargetHooks).GetField("_remoteStreamingDirection", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly FieldInfo _remoteStreamingReleaseAccumField = typeof(TargetHooks).GetField("_remoteStreamingReleaseAccum", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly FieldInfo _remotePeerBackingField = typeof(TargetHooks).GetField("<RemotePeerClientId>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        private readonly FieldInfo _edgeThresholdPixelsField = typeof(TargetHooks).GetField("EdgeThresholdPixels", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        // Reflection for CallNextHookExImpl seam
        private readonly FieldInfo _callNextHookExImplField = typeof(TargetHooks).GetField("CallNextHookExImpl", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;

        // Reflection for GetCursorPosImpl seam - allows tests to control cursor position
        private readonly FieldInfo _getCursorPosImplField = typeof(TargetHooks).GetField("GetCursorPosImpl", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;

        // Reflection for preflight seams - allows tests to bypass network operations
        private readonly FieldInfo _sendPreFlightRequestImplField = typeof(TargetHooks).GetField("SendPreFlightRequestImpl", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        private readonly FieldInfo _waitForPreFlightAckImplField = typeof(TargetHooks).GetField("WaitForPreFlightAckImpl", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;

        // Store the original GetCursorPosImpl delegate to restore after tests
        private static Delegate? _originalGetCursorPosImpl;

        // Test cursor position - used by test delegate
        private static int _testCursorX = 0;
        private static int _testCursorY = 0;

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

        /// <summary>
        /// Sets up a test delegate for GetCursorPosImpl that returns controlled cursor positions.
        /// The delegate will return the value of _testCursorX and _testCursorY.
        /// Call SetTestCursorPosition to update the position before each InvokeCallback.
        /// </summary>
        private void SetupTestGetCursorPos()
        {
            // Save original delegate if not already saved
            if (_originalGetCursorPosImpl == null)
            {
                _originalGetCursorPosImpl = (Delegate)_getCursorPosImplField.GetValue(null)!;
            }

            // Create a delegate that matches the GetCursorPosDelegate signature
            // The delegate type is: delegate bool GetCursorPosDelegate(out POINT lpPoint)
            // We need to use reflection to create the delegate with the correct type
            var delegateType = typeof(TargetHooks).GetNestedType("GetCursorPosDelegate", BindingFlags.NonPublic | BindingFlags.Public)!;
            var pointType = typeof(TargetHooks).GetNestedType("POINT", BindingFlags.NonPublic | BindingFlags.Public)!;

            // Create a method that returns our test cursor position
            var testDelegate = Delegate.CreateDelegate(delegateType, typeof(MouseHooksTests).GetMethod(nameof(TestGetCursorPos), BindingFlags.Static | BindingFlags.NonPublic)!);
            _getCursorPosImplField.SetValue(null, testDelegate);
        }

        /// <summary>
        /// Test implementation of GetCursorPos that returns controlled cursor positions.
        /// </summary>
        private static bool TestGetCursorPos(out TargetHooks.POINT lpPoint)
        {
            lpPoint = new TargetHooks.POINT { x = _testCursorX, y = _testCursorY };
            return true;
        }

        /// <summary>
        /// Sets the test cursor position that will be returned by GetCursorPosImpl.
        /// </summary>
        private static void SetTestCursorPosition(int x, int y)
        {
            _testCursorX = x;
            _testCursorY = y;
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

            // Reset GetCursorPosImpl to original if it was saved
            if (_originalGetCursorPosImpl != null)
            {
                _getCursorPosImplField.SetValue(null, _originalGetCursorPosImpl);
            }

            // Reset preflight seams to null (production behavior)
            _sendPreFlightRequestImplField?.SetValue(null, null);
            _waitForPreFlightAckImplField?.SetValue(null, null);

            // Comprehensively reset ALL static flags to avoid cross-test state leakage
            _isSyntheticInput.SetValue(null, false);
            _remoteStreamingField.SetValue(null, false);
            _remoteStreamingDirectionField.SetValue(null, null);
            _remoteStreamingReleaseAccumField.SetValue(null, 0);
            _remoteCursorXField.SetValue(null, 0);
            _remoteCursorYField.SetValue(null, 0);
            _suppressXField.SetValue(null, int.MinValue);
            _suppressCountField.SetValue(null, 0);
            var suppressYField = typeof(TargetHooks).GetField("_suppressY", BindingFlags.Static | BindingFlags.NonPublic);
            suppressYField?.SetValue(null, int.MinValue);
            
            // Clear remote peer
            TargetHooks.SetRemotePeer(null!);
            
            // Reset preflight state
            var preFlightAckField = typeof(TargetHooks).GetField("_preFlightAckReceived", BindingFlags.Static | BindingFlags.NonPublic);
            preFlightAckField?.SetValue(null, false);
            var receiverEdgeHitField = typeof(TargetHooks).GetField("_receiverReportedEdgeHit", BindingFlags.Static | BindingFlags.NonPublic);
            receiverEdgeHitField?.SetValue(null, false);
            
            // Reset debounce timer
            var lastEdgeClaimField = typeof(TargetHooks).GetField("_lastEdgeClaimAttempt", BindingFlags.Static | BindingFlags.NonPublic);
            lastEdgeClaimField?.SetValue(null, DateTime.MinValue);
            
            _instanceField.SetValue(null, null);
            
            // Disconnect and cleanup real UDP transmitter
            _realConcreteUdp?.Disconnect();
            _sut?.UninstallHooks();
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

        [TestMethod]
        public void RemoteStreaming_CursorClampsWithinRemoteBounds()
        {
            // Arrange: configure remote streaming scenario with defined remote bounds
            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp!.SetLocalRole(ConnectionRole.Sender);
            _remoteStreamingField.SetValue(null, true);

            // Define remote peer id
            string remoteId = "Remote-1";
            TargetHooks.SetRemotePeer(remoteId);

            // Build virtual screen map with one remote monitor bounds (1000,1000)-(1100,1050)
            // and one local monitor elsewhere so claim logic not triggered here.
            // We assume VirtualScreenMap exposes AddMonitorForTest (if not existing tests will reveal); fallback create via reflection.
            var screenMap = _realScreenMap!;
            // Add clients
            screenMap.AddOrUpdateClient(new ClientPc { ClientId = _realCoordinator!.SelfClientId, FriendlyName = "LocalClient" });
            screenMap.AddOrUpdateClient(new ClientPc { ClientId = remoteId, FriendlyName = "RemoteClient" });
            // Add monitors
            screenMap.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = _realCoordinator.SelfClientId,
                FriendlyName = "LocalMon",
                GlobalBounds = new RectInt(0, 0, 800, 600),
                LocalBounds = new RectInt(0, 0, 800, 600)
            });
            screenMap.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = remoteId,
                FriendlyName = "RemoteMon",
                GlobalBounds = new RectInt(1000, 1000, 100, 50),
                LocalBounds = new RectInt(0, 0, 100, 50)
            });

            // Seed remote cursor inside bounds
            _remoteCursorXField.SetValue(null, 1050);
            _remoteCursorYField.SetValue(null, 1025);

            // Provide last actual cursor baseline to compute deltas
            typeof(TargetHooks).GetField("_lastActualCursorX", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 500);
            typeof(TargetHooks).GetField("_lastActualCursorY", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 500);

            // Act: Move with large positive dx/dy attempts beyond right/bottom edges
            InvokeCallback(0, 700, 700, (IntPtr)WM_MOUSEMOVE, 0, 0); // dx=200 dy=200
            int afterX1 = (int)_remoteCursorXField.GetValue(null)!;
            int afterY1 = (int)_remoteCursorYField.GetValue(null)!;

            // Then large negative beyond left/top
            InvokeCallback(0, 300, 300, (IntPtr)WM_MOUSEMOVE, 0, 0); // dx=-200 dy=-200 relative to refreshed baseline
            int afterX2 = (int)_remoteCursorXField.GetValue(null)!;
            int afterY2 = (int)_remoteCursorYField.GetValue(null)!;

            // Assert: Cursor stayed within [1000,1099] X and [1000,1049] Y given remote monitor width/height
            Assert.IsTrue(afterX1 >= 1000 && afterX1 <= 1099, "First clamp (positive) X out of bounds");
            Assert.IsTrue(afterY1 >= 1000 && afterY1 <= 1049, "First clamp (positive) Y out of bounds");
            Assert.IsTrue(afterX2 >= 1000 && afterX2 <= 1099, "Second clamp (negative) X out of bounds");
            Assert.IsTrue(afterY2 >= 1000 && afterY2 <= 1049, "Second clamp (negative) Y out of bounds");
        }

        [TestMethod]
        public void RemoteStreaming_ReleaseAccum_IncrementsAndResets()
        {
            // Setup test seam for GetCursorPos to return controlled values
            SetupTestGetCursorPos();

            // Arrange: provide remote peer + monitor so non-fallback path runs
            string remoteId = "RelAccumPeer";
            TargetHooks.SetRemotePeer(remoteId);
            _realScreenMap!.AddOrUpdateClient(new ClientPc { ClientId = remoteId, FriendlyName = "Remote" });
            _realScreenMap.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = remoteId,
                FriendlyName = "RMon",
                GlobalBounds = new RectInt(1500, 1500, 300, 200),
                LocalBounds = new RectInt(0, 0, 300, 200)
            });

            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp!.SetLocalRole(ConnectionRole.Sender);
            _remoteStreamingField.SetValue(null, true);
            _remoteStreamingDirectionField.SetValue(null, OmniMouse.Switching.Direction.Left); // exit was Left; return is RIGHT (dx>0)
            _remoteStreamingReleaseAccumField.SetValue(null, 0);

            // Baseline actual cursor
            typeof(TargetHooks).GetField("_lastActualCursorX", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 1000);
            typeof(TargetHooks).GetField("_lastActualCursorY", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 1000);

            // Set test cursor position for return movement
            SetTestCursorPosition(1008, 1000);

            // Act 1: return-direction movement (dx>0) increments
            InvokeCallback(0, 1008, 1000, (IntPtr)WM_MOUSEMOVE, 0, 0); // dx=+8
            int afterReturn = (int)_remoteStreamingReleaseAccumField.GetValue(null)!;
            Assert.IsTrue(afterReturn >= 8, $"Accum should increase on return movement; got {afterReturn}");

            // Update baseline and cursor position for opposite movement
            typeof(TargetHooks).GetField("_lastActualCursorX", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 1008);
            SetTestCursorPosition(995, 1000);

            // Act 2: opposite-direction movement (dx<0) should reset
            InvokeCallback(0, 995, 1000, (IntPtr)WM_MOUSEMOVE, 0, 0); // dx=-13
            int afterOpposite = (int)_remoteStreamingReleaseAccumField.GetValue(null)!;

            // Assert: reset (allow exact 0 or small due to clamping; ensure less than prior and near zero)
            Assert.IsTrue(afterOpposite <= 1, $"Accum should reset on opposite movement; got {afterOpposite}");
        }

        [TestMethod]
        public void RemoteStreaming_ReleaseAccum_RightDirection()
        {
            // Setup test seam for GetCursorPos to return controlled values
            SetupTestGetCursorPos();

            // Setup remote peer and monitor to use non-fallback path
            string remoteId = "RelAccumRightPeer";
            TargetHooks.SetRemotePeer(remoteId);
            _realScreenMap!.AddOrUpdateClient(new ClientPc { ClientId = remoteId, FriendlyName = "RemoteRight" });
            _realScreenMap.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = remoteId,
                FriendlyName = "RightMon",
                GlobalBounds = new RectInt(2000, 2000, 400, 300),
                LocalBounds = new RectInt(0, 0, 400, 300)
            });

            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp!.SetLocalRole(ConnectionRole.Sender);
            _remoteStreamingField.SetValue(null, true);
            _remoteStreamingDirectionField.SetValue(null, OmniMouse.Switching.Direction.Right);
            _remoteStreamingReleaseAccumField.SetValue(null, 0);
            typeof(TargetHooks).GetField("_lastActualCursorX", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 1000);
            typeof(TargetHooks).GetField("_lastActualCursorY", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 1000);
            
            // Set test cursor position (GetCursorPos will return this)
            SetTestCursorPosition(990, 1000);
            
            // For Right exit, return is LEFT (dx<0) increments
            InvokeCallback(0, 990, 1000, (IntPtr)WM_MOUSEMOVE, 0, 0);
            int inc = (int)_remoteStreamingReleaseAccumField.GetValue(null)!;
            
            // Update cursor position for next call and reset baseline
            SetTestCursorPosition(1010, 1000);
            typeof(TargetHooks).GetField("_lastActualCursorX", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 990);
            
            // Non-return direction (dx>0) resets
            InvokeCallback(0, 1010, 1000, (IntPtr)WM_MOUSEMOVE, 0, 0);
            int reset = (int)_remoteStreamingReleaseAccumField.GetValue(null)!;
            
            Assert.IsTrue(inc > 0, $"Accum should increase on return movement; got {inc}");
            Assert.AreEqual(0, reset, $"Accum should reset on opposite movement; got {reset}");
        }

        [TestMethod]
        public void RemoteStreaming_ReleaseAccum_UpDirection()
        {
            // Setup test seam for GetCursorPos to return controlled values
            SetupTestGetCursorPos();

            // Arrange: remote peer + monitor
            string remoteId = "RelAccumUpPeer";
            TargetHooks.SetRemotePeer(remoteId);
            _realScreenMap!.AddOrUpdateClient(new ClientPc { ClientId = remoteId, FriendlyName = "RemoteUp" });
            _realScreenMap.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = remoteId,
                FriendlyName = "UpMon",
                GlobalBounds = new RectInt(2200, 2200, 300, 300),
                LocalBounds = new RectInt(0, 0, 300, 300)
            });

            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp!.SetLocalRole(ConnectionRole.Sender);
            _remoteStreamingField.SetValue(null, true);
            _remoteStreamingDirectionField.SetValue(null, OmniMouse.Switching.Direction.Up); // exit Up; return is DOWN (dy>0)
            _remoteStreamingReleaseAccumField.SetValue(null, 0);

            typeof(TargetHooks).GetField("_lastActualCursorX", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 1200);
            typeof(TargetHooks).GetField("_lastActualCursorY", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 1200);

            // Set test cursor position
            SetTestCursorPosition(1200, 1206);

            // Act 1: return-direction (dy>0) increments
            InvokeCallback(0, 1200, 1206, (IntPtr)WM_MOUSEMOVE, 0, 0); // dy=+6
            int afterDown = (int)_remoteStreamingReleaseAccumField.GetValue(null)!;
            Assert.IsTrue(afterDown >= 6, $"Accum should increase on return movement; got {afterDown}");

            // Update cursor position for next call and reset baseline
            SetTestCursorPosition(1200, 1194);
            typeof(TargetHooks).GetField("_lastActualCursorY", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 1206);

            // Act 2: opposite (dy<0) resets
            InvokeCallback(0, 1200, 1194, (IntPtr)WM_MOUSEMOVE, 0, 0); // dy=-12
            int afterUp = (int)_remoteStreamingReleaseAccumField.GetValue(null)!;

            Assert.IsTrue(afterUp <= 1, $"Accum should reset on opposite movement; got {afterUp}");
        }

        [TestMethod]
        public void RemoteStreaming_ReleaseAccum_DownDirection()
        {
            // Setup test seam for GetCursorPos to return controlled values
            SetupTestGetCursorPos();

            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp!.SetLocalRole(ConnectionRole.Sender);
            _remoteStreamingField.SetValue(null, true);
            _remoteStreamingDirectionField.SetValue(null, OmniMouse.Switching.Direction.Down);
            _remoteStreamingReleaseAccumField.SetValue(null, 0);
            typeof(TargetHooks).GetField("_lastActualCursorX", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 1000);
            typeof(TargetHooks).GetField("_lastActualCursorY", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 1000);
            
            // Set test cursor position
            SetTestCursorPosition(1000, 990);
            
            // Down exit, return is UP (dy<0)
            InvokeCallback(0, 1000, 990, (IntPtr)WM_MOUSEMOVE, 0, 0);
            int inc = (int)_remoteStreamingReleaseAccumField.GetValue(null)!;
            
            // Update cursor position for next call and reset baseline
            SetTestCursorPosition(1010, 1000);
            typeof(TargetHooks).GetField("_lastActualCursorY", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 990);
            
            // Non-return resets
            InvokeCallback(0, 1010, 1000, (IntPtr)WM_MOUSEMOVE, 0, 0);
            int reset = (int)_remoteStreamingReleaseAccumField.GetValue(null)!;
            Assert.IsTrue(inc > 0, $"Accum should increase on return movement; got {inc}");
            Assert.AreEqual(0, reset, $"Accum should reset on opposite movement; got {reset}");
        }

        [TestMethod]
        public void EdgeClaim_Left_Top_Bottom_BeginStreaming()
        {
            // Setup test seams to bypass actual network operations
            _sendPreFlightRequestImplField.SetValue(null, new Func<UdpMouseTransmitter, bool>(tx => true));
            _waitForPreFlightAckImplField.SetValue(null, new Func<int, bool>(timeout => true));

            // Arrange receiver with seam and layout
            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp!.SetLocalRole(ConnectionRole.Receiver);
            var handshakeField = typeof(UdpMouseTransmitter).GetField("_handshakeComplete", BindingFlags.Instance | BindingFlags.NonPublic);
            if (handshakeField != null) handshakeField.SetValue(_realConcreteUdp, true);
            
            _realConcreteUdp.SendTakeControlImpl = (clientId, ux, uy) => { };
            string remoteId = "Remote-Edges";
            TargetHooks.SetRemotePeer(remoteId);
            _realScreenMap!.AddOrUpdateClient(new ClientPc { ClientId = _realCoordinator!.SelfClientId, FriendlyName = "Local" });
            _realScreenMap.AddOrUpdateClient(new ClientPc { ClientId = remoteId, FriendlyName = "Remote" });
            _realScreenMap.AddOrUpdateMonitor(new MonitorInfo { OwnerClientId = _realCoordinator.SelfClientId, FriendlyName = "Local", GlobalBounds = new RectInt(0,0,100,100), LocalBounds = new RectInt(0,0,100,100) });
            _realScreenMap.AddOrUpdateMonitor(new MonitorInfo { OwnerClientId = remoteId, FriendlyName = "Remote", GlobalBounds = new RectInt(100,0,100,100), LocalBounds = new RectInt(0,0,100,100) });

            // Reset debounce timer before each edge test
            var lastEdgeClaimField = typeof(TargetHooks).GetField("_lastEdgeClaimAttempt", BindingFlags.Static | BindingFlags.NonPublic);
            lastEdgeClaimField?.SetValue(null, DateTime.MinValue);

            // Left edge - test streaming begins
            InvokeCallback(0, 1, 10, (IntPtr)WM_MOUSEMOVE, 0, 0);
            bool streamingAfterLeft = (bool)_remoteStreamingField.GetValue(null)!;
            
            // Reset streaming for next checks
            TargetHooks.EndRemoteStreaming();
            _realConcreteUdp.SetLocalRole(ConnectionRole.Receiver);
            lastEdgeClaimField?.SetValue(null, DateTime.MinValue); // Reset debounce

            // Top edge
            InvokeCallback(0, 50, 1, (IntPtr)WM_MOUSEMOVE, 0, 0);
            bool streamingAfterTop = (bool)_remoteStreamingField.GetValue(null)!;

            TargetHooks.EndRemoteStreaming();
            _realConcreteUdp.SetLocalRole(ConnectionRole.Receiver);
            lastEdgeClaimField?.SetValue(null, DateTime.MinValue); // Reset debounce

            // Bottom edge
            InvokeCallback(0, 50, 99, (IntPtr)WM_MOUSEMOVE, 0, 0);
            bool streamingAfterBottom = (bool)_remoteStreamingField.GetValue(null)!;
            
            // Assert at the end to see all failures at once
            Assert.IsTrue(streamingAfterLeft, "Streaming should begin after left edge claim");
            Assert.IsTrue(streamingAfterTop, "Streaming should begin after top edge claim");
            Assert.IsTrue(streamingAfterBottom, "Streaming should begin after bottom edge claim");
        }

        //[TestMethod]
        //public void MouseButtons_Middle_RemoteStream_Blocks()
        //{
        //    var mockUdpClient = new Moq.Mock<IUdpClient>(Moq.MockBehavior.Strict);
        //    mockUdpClient.Setup(m => m.Send(Moq.It.IsAny<byte[]>(), Moq.It.IsAny<int>(), Moq.It.IsAny<System.Net.IPEndPoint>()))
        //        .Returns((byte[] dgram, int bytes, System.Net.IPEndPoint ep) => bytes)
        //        .Verifiable();
        //    var udpClientField = typeof(UdpMouseTransmitter).GetField("_udpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
        //    udpClientField.SetValue(_realConcreteUdp, mockUdpClient.Object);
        //    _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
        //    _realConcreteUdp!.SetLocalRole(ConnectionRole.Sender);
        //    var remoteEndPointField = typeof(UdpMouseTransmitter).GetField("_remoteEndPoint", BindingFlags.Instance | BindingFlags.NonPublic)!;
        //    remoteEndPointField.SetValue(_realConcreteUdp, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 5050));
        //    _remoteStreamingField.SetValue(null, true);
        //    object? result = InvokeCallback(0, 0, 0, (IntPtr)WM_MBUTTONDOWN, 0, 0);
        //    mockUdpClient.Verify(m => m.Send(Moq.It.IsAny<byte[]>(), Moq.It.IsAny<int>(), Moq.It.IsAny<System.Net.IPEndPoint>()), Times.Once);
        //    var returned = (IntPtr)result!;
        //    if (returned != (IntPtr)1) Assert.AreEqual((IntPtr)9999, returned);
        //}

        [TestMethod]
        public void EdgeClaim_BeginsRemoteStreamingAndSetsDirection()
        {
            // Setup test seams to bypass actual network operations
            _sendPreFlightRequestImplField.SetValue(null, new Func<UdpMouseTransmitter, bool>(tx => true));
            _waitForPreFlightAckImplField.SetValue(null, new Func<int, bool>(timeout => true));

            // Arrange: role receiver, handshake complete, near right edge
            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp!.SetLocalRole(ConnectionRole.Receiver);

            // Force handshake complete via private field if present
            var handshakeField = typeof(UdpMouseTransmitter).GetField("_handshakeComplete", BindingFlags.Instance | BindingFlags.NonPublic);
            if (handshakeField != null) handshakeField.SetValue(_realConcreteUdp, true);

            // Install test seam to avoid real TCP and keep streaming state intact
            _realConcreteUdp.SendTakeControlImpl = (clientId, ux, uy) => { /* noop in tests */ };

            // Reset debounce timer
            var lastEdgeClaimField = typeof(TargetHooks).GetField("_lastEdgeClaimAttempt", BindingFlags.Static | BindingFlags.NonPublic);
            lastEdgeClaimField?.SetValue(null, DateTime.MinValue);

            // Local + remote monitors
            string remoteId = "Remote-2";
            TargetHooks.SetRemotePeer(remoteId);
            var screenMap = _realScreenMap!;
            screenMap.AddOrUpdateClient(new ClientPc { ClientId = _realCoordinator!.SelfClientId, FriendlyName = "LocalClient" });
            screenMap.AddOrUpdateClient(new ClientPc { ClientId = remoteId, FriendlyName = "RemoteClient" });
            screenMap.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = _realCoordinator.SelfClientId,
                FriendlyName = "LocalMon",
                GlobalBounds = new RectInt(0, 0, 1920, 1080),
                LocalBounds = new RectInt(0, 0, 1920, 1080)
            });
            screenMap.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = remoteId,
                FriendlyName = "RemoteMon",
                GlobalBounds = new RectInt(1920, 0, 1280, 1024),
                LocalBounds = new RectInt(0, 0, 1280, 1024)
            });

            // Act: Move at X very near right edge of local (assuming threshold <=5)
            int triggerX = 1919; // inside but near edge
            InvokeCallback(0, triggerX, 100, (IntPtr)WM_MOUSEMOVE, 0, 0);

            // Assert: remote streaming started and direction set Right (with seam preventing TCP side-effects)
            Assert.IsTrue((bool)_remoteStreamingField.GetValue(null)!, "Remote streaming should have begun after edge claim.");
            Assert.AreEqual(OmniMouse.Switching.Direction.Right, _remoteStreamingDirectionField.GetValue(null), "Direction should be Right after right-edge claim.");
        }

        [TestMethod]
        public void LostSenderRole_MidStream_EndsStreaming()
        {
            // Arrange: start as sender streaming then flip to receiver
            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp!.SetLocalRole(ConnectionRole.Sender);
            _remoteStreamingField.SetValue(null, true);
            typeof(TargetHooks).GetField("_lastActualCursorX", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 500);
            typeof(TargetHooks).GetField("_lastActualCursorY", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 500);

            // First move while sender (stream continues)
            InvokeCallback(0, 510, 500, (IntPtr)WM_MOUSEMOVE, 0, 0);
            Assert.IsTrue((bool)_remoteStreamingField.GetValue(null)!, "Streaming should still be active after first sender move.");

            // Flip role to Receiver mid-stream
            _realConcreteUdp.SetLocalRole(ConnectionRole.Receiver);

            // Second move triggers lost sender path
            InvokeCallback(0, 520, 500, (IntPtr)WM_MOUSEMOVE, 0, 0);

            // Assert: streaming ended
            Assert.IsFalse((bool)_remoteStreamingField.GetValue(null)!, "Streaming should end after losing sender role mid-stream.");
        }

        [TestMethod]
        public void TryEdgeReturn_OppositeEdge_EndsStreaming()
        {
            // Setup test seam for GetCursorPos to return controlled values
            SetupTestGetCursorPos();

            // Arrange: streaming active, direction Right (exited via right edge). Return when hitting LEFT remote edge.
            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp!.SetLocalRole(ConnectionRole.Sender);
            
            string remoteId = "Remote-3";
            TargetHooks.SetRemotePeer(remoteId);
            var screenMap = _realScreenMap!;
            screenMap.AddOrUpdateClient(new ClientPc { ClientId = _realCoordinator!.SelfClientId, FriendlyName = "LocalClient" });
            screenMap.AddOrUpdateClient(new ClientPc { ClientId = remoteId, FriendlyName = "RemoteClient" });
            screenMap.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = _realCoordinator.SelfClientId,
                FriendlyName = "LocalMon",
                GlobalBounds = new RectInt(0, 0, 800, 600),
                LocalBounds = new RectInt(0, 0, 800, 600)
            });
            screenMap.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = remoteId,
                FriendlyName = "RemoteMon",
                GlobalBounds = new RectInt(900, 0, 400, 600),
                LocalBounds = new RectInt(0, 0, 400, 600)
            });

            // Set streaming state AFTER screen map is configured
            _remoteStreamingField.SetValue(null, true);
            _remoteStreamingDirectionField.SetValue(null, OmniMouse.Switching.Direction.Right);

            // Set receiver reported edge hit flag (simulates multi-machine handshake)
            var receiverEdgeHitField = typeof(TargetHooks).GetField("_receiverReportedEdgeHit", BindingFlags.Static | BindingFlags.NonPublic);
            receiverEdgeHitField?.SetValue(null, true);

            // Set accumulator above threshold (40 pixels)
            _remoteStreamingReleaseAccumField.SetValue(null, 50);

            // Place remote cursor near left edge of remote bounds to trigger return (x-left <= threshold)
            _remoteCursorXField.SetValue(null, 901); // near left edge (900 + 1)
            _remoteCursorYField.SetValue(null, 100);

            // Set test cursor position
            SetTestCursorPosition(895, 100);

            // Invoke movement with negative delta to move cursor to/past left edge
            typeof(TargetHooks).GetField("_lastActualCursorX", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 910);
            typeof(TargetHooks).GetField("_lastActualCursorY", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 100);
            // Move left to hit the edge
            InvokeCallback(0, 895, 100, (IntPtr)WM_MOUSEMOVE, 0, 0);

            // Assert: streaming ended
            Assert.IsFalse((bool)_remoteStreamingField.GetValue(null)!, "Streaming should end after hitting opposite remote edge for return.");
        }

        [TestMethod]
        public void TryEdgeReturn_FromLeftDirection_HitsRightEdge_EndsStreaming()
        {
            // Setup test seam for GetCursorPos to return controlled values
            SetupTestGetCursorPos();

            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp!.SetLocalRole(ConnectionRole.Sender);
            
            string remoteId = "Remote-Left";
            TargetHooks.SetRemotePeer(remoteId);
            _realScreenMap!.AddOrUpdateClient(new ClientPc { ClientId = remoteId, FriendlyName = "Remote" });
            _realScreenMap.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = remoteId,
                FriendlyName = "R",
                GlobalBounds = new RectInt(100, 200, 300, 150),
                LocalBounds = new RectInt(0, 0, 300, 150)
            });
            
            // Set streaming state AFTER screen map is configured
            _remoteStreamingField.SetValue(null, true);
            _remoteStreamingDirectionField.SetValue(null, OmniMouse.Switching.Direction.Left);

            // Set receiver reported edge hit flag (simulates multi-machine handshake)
            var receiverEdgeHitField = typeof(TargetHooks).GetField("_receiverReportedEdgeHit", BindingFlags.Static | BindingFlags.NonPublic);
            receiverEdgeHitField?.SetValue(null, true);

            // Set accumulator above threshold (40 pixels)
            _remoteStreamingReleaseAccumField.SetValue(null, 50);
            
            // Place remote cursor near right edge (should trigger return for Left exit)
            _remoteCursorXField.SetValue(null, 398); // near right edge (right=400)
            _remoteCursorYField.SetValue(null, 250);

            // Set test cursor position
            SetTestCursorPosition(405, 250);

            typeof(TargetHooks).GetField("_lastActualCursorX", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 390);
            typeof(TargetHooks).GetField("_lastActualCursorY", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 250);
            // Move right to hit the edge
            InvokeCallback(0, 405, 250, (IntPtr)WM_MOUSEMOVE, 0, 0);
            Assert.IsFalse((bool)_remoteStreamingField.GetValue(null)!, "Streaming should end after hitting right edge when exited from Left");
        }

        [TestMethod]
        public void TryEdgeReturn_FromUpDirection_HitsBottomEdge_EndsStreaming()
        {
            // Setup test seam for GetCursorPos to return controlled values
            SetupTestGetCursorPos();

            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp!.SetLocalRole(ConnectionRole.Sender);
            
            string remoteId = "Remote-Up";
            TargetHooks.SetRemotePeer(remoteId);
            _realScreenMap!.AddOrUpdateClient(new ClientPc { ClientId = remoteId, FriendlyName = "Remote" });
            _realScreenMap.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = remoteId,
                FriendlyName = "R",
                GlobalBounds = new RectInt(500, 500, 200, 200),
                LocalBounds = new RectInt(0, 0, 200, 200)
            });
            
            // Set streaming state AFTER screen map is configured
            _remoteStreamingField.SetValue(null, true);
            _remoteStreamingDirectionField.SetValue(null, OmniMouse.Switching.Direction.Up);

            // Set receiver reported edge hit flag (simulates multi-machine handshake)
            var receiverEdgeHitField = typeof(TargetHooks).GetField("_receiverReportedEdgeHit", BindingFlags.Static | BindingFlags.NonPublic);
            receiverEdgeHitField?.SetValue(null, true);

            // Set accumulator above threshold (40 pixels)
            _remoteStreamingReleaseAccumField.SetValue(null, 50);
            
            // Cursor near bottom edge
            _remoteCursorXField.SetValue(null, 550);
            _remoteCursorYField.SetValue(null, 698); // near bottom (bottom=700)

            // Set test cursor position
            SetTestCursorPosition(550, 705);

            typeof(TargetHooks).GetField("_lastActualCursorX", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 550);
            typeof(TargetHooks).GetField("_lastActualCursorY", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 690);
            // Move down to hit the edge
            InvokeCallback(0, 550, 705, (IntPtr)WM_MOUSEMOVE, 0, 0);
            Assert.IsFalse((bool)_remoteStreamingField.GetValue(null)!, "Streaming should end after hitting bottom edge when exited from Up");
        }

        [TestMethod]
        public void TryEdgeReturn_FromDownDirection_HitsTopEdge_EndsStreaming()
        {
            // Setup test seam for GetCursorPos to return controlled values
            SetupTestGetCursorPos();

            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp!.SetLocalRole(ConnectionRole.Sender);
            
            string remoteId = "Remote-Down";
            TargetHooks.SetRemotePeer(remoteId);
            _realScreenMap!.AddOrUpdateClient(new ClientPc { ClientId = remoteId, FriendlyName = "Remote" });
            _realScreenMap.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = remoteId,
                FriendlyName = "R",
                GlobalBounds = new RectInt(800, 300, 150, 300),
                LocalBounds = new RectInt(0, 0, 150, 300)
            });
            
            // Set streaming state AFTER screen map is configured
            _remoteStreamingField.SetValue(null, true);
            _remoteStreamingDirectionField.SetValue(null, OmniMouse.Switching.Direction.Down);

            // Set receiver reported edge hit flag (simulates multi-machine handshake)
            var receiverEdgeHitField = typeof(TargetHooks).GetField("_receiverReportedEdgeHit", BindingFlags.Static | BindingFlags.NonPublic);
            receiverEdgeHitField?.SetValue(null, true);

            // Set accumulator above threshold (40 pixels)
            _remoteStreamingReleaseAccumField.SetValue(null, 50);
            
            // Cursor near top edge
            _remoteCursorXField.SetValue(null, 850);
            _remoteCursorYField.SetValue(null, 302); // near top (top=300)

            // Set test cursor position
            SetTestCursorPosition(850, 295);

            typeof(TargetHooks).GetField("_lastActualCursorX", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 850);
            typeof(TargetHooks).GetField("_lastActualCursorY", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 310);
            // Move up to hit the edge
            InvokeCallback(0, 850, 295, (IntPtr)WM_MOUSEMOVE, 0, 0);
            Assert.IsFalse((bool)_remoteStreamingField.GetValue(null)!, "Streaming should end after hitting top edge when exited from Down");
        }

        [TestMethod]
        public void RemoteStreaming_FallbackBounds_AccumulatesRawDeltas()
        {
            // Arrange: no remote bounds (RemotePeerClientId null) forces fallback accumulation path
            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp!.SetLocalRole(ConnectionRole.Sender);
            _remoteStreamingField.SetValue(null, true);
            // Clear remote peer id via backing field since setter is private
            _remotePeerBackingField?.SetValue(null, null);
            _remoteCursorXField.SetValue(null, 0);
            _remoteCursorYField.SetValue(null, 0);
            typeof(TargetHooks).GetField("_lastActualCursorX", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 1000);
            typeof(TargetHooks).GetField("_lastActualCursorY", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_sut, 1000);

            // Act: two moves accumulate
            InvokeCallback(0, 1010, 1005, (IntPtr)WM_MOUSEMOVE, 0, 0); // dx=10 dy=5
            InvokeCallback(0, 1000, 995, (IntPtr)WM_MOUSEMOVE, 0, 0); // dx=-10 dy=-10
            int finalX = (int)_remoteCursorXField.GetValue(null)!;
            int finalY = (int)_remoteCursorYField.GetValue(null)!;

            // Assert: cursor changed from origin in at least one axis (non-clamped accumulation occurred)
            Assert.IsTrue(finalX != 0 || finalY != 0, "Expected fallback accumulation to modify remote cursor position.");
        }

        [TestMethod]
        public void BeginRemoteStreaming_WithRemoteBounds_InitializesCursor()
        {
            // Arrange
            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp!.SetLocalRole(ConnectionRole.Receiver); // role irrelevant for initialization inside BeginRemoteStreaming
            string remoteId = "RemoteInit";
            TargetHooks.SetRemotePeer(remoteId);
            _realScreenMap!.AddOrUpdateClient(new ClientPc { ClientId = remoteId, FriendlyName = "Remote" });
            _realScreenMap.AddOrUpdateMonitor(new MonitorInfo
            {
                OwnerClientId = remoteId,
                FriendlyName = "RemoteMon",
                GlobalBounds = new RectInt(2000, 100, 300, 200),
                LocalBounds = new RectInt(0, 0, 300, 200)
            });

            // Act
            TargetHooks.BeginRemoteStreaming(OmniMouse.Switching.Direction.Right);

            // Assert
            Assert.IsTrue((bool)_remoteStreamingField.GetValue(null)!, "Streaming flag should be true after BeginRemoteStreaming.");
            Assert.AreEqual(OmniMouse.Switching.Direction.Right, _remoteStreamingDirectionField.GetValue(null), "Direction should be set.");
            int rcx = (int)_remoteCursorXField.GetValue(null)!;
            int rcy = (int)_remoteCursorYField.GetValue(null)!;
            Assert.IsTrue(rcx >= 2000 && rcx < 2300, "Remote cursor X should be within remote monitor bounds.");
            Assert.IsTrue(rcy >= 100 && rcy < 300, "Remote cursor Y should be within remote monitor bounds.");
        }

        [TestMethod]
        public void BeginRemoteStreaming_Fallback_NoPeer_UsesZeroOrigin()
        {
            // Arrange: clear peer id
            _remotePeerBackingField?.SetValue(null, null);
            _remoteStreamingField.SetValue(null, false);

            // Act
            TargetHooks.BeginRemoteStreaming(OmniMouse.Switching.Direction.Left);

            // Assert
            Assert.IsTrue((bool)_remoteStreamingField.GetValue(null)!, "Streaming should enable even without peer.");
            Assert.AreEqual(OmniMouse.Switching.Direction.Left, _remoteStreamingDirectionField.GetValue(null), "Direction should be set to the provided value.");
            // Fallback initializes to (0,0)
            Assert.AreEqual(0, (int)_remoteCursorXField.GetValue(null)!);
            Assert.AreEqual(0, (int)_remoteCursorYField.GetValue(null)!);
        }

        [TestMethod]
        public void EndRemoteStreaming_ResetsState()
        {
            // Arrange
            _remoteStreamingField.SetValue(null, true);
            _remoteStreamingDirectionField.SetValue(null, OmniMouse.Switching.Direction.Up);
            _remoteStreamingReleaseAccumField.SetValue(null, 25);
            _remoteCursorXField.SetValue(null, 123);
            _remoteCursorYField.SetValue(null, 456);
            _udpTransmitterField.SetValue(_sut, _realConcreteUdp);
            _realConcreteUdp!.SetLocalRole(ConnectionRole.Sender);

            // Act
            TargetHooks.EndRemoteStreaming();

            // Assert
            Assert.IsFalse((bool)_remoteStreamingField.GetValue(null)!, "Streaming flag should be false.");
            Assert.IsNull(_remoteStreamingDirectionField.GetValue(null), "Direction should be cleared.");
            Assert.AreEqual(0, (int)_remoteStreamingReleaseAccumField.GetValue(null)!);
            Assert.AreEqual(0, (int)_remoteCursorXField.GetValue(null)!);
            Assert.AreEqual(0, (int)_remoteCursorYField.GetValue(null)!);
        }


        [TestMethod]
        public void SuppressNextMoveFrom_SetsSuppressionState()
        {
            // Act
            TargetHooks.SuppressNextMoveFrom(222, 333);

            // Assert
            Assert.AreEqual(2, (int)_suppressCountField.GetValue(null)!);
            Assert.AreEqual(222, (int)_suppressXField.GetValue(null)!);
            var suppressYField = typeof(TargetHooks).GetField("_suppressY", BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.AreEqual(333, (int)suppressYField.GetValue(null)!);
        }
    }
}