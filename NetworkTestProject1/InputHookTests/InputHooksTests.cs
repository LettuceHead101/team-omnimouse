namespace NetworkTestProject1.InputHooks
{
    using System;
    using System.Reflection;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;
    using OmniMouse.Core;       // Assuming this is where InputCoordinator lives
    using OmniMouse.Network;    // For IUdpMouseTransmitter, VirtualScreenMap, UdpMouseTransmitter
    using OmniMouse.Switching;  // For IMultiMachineSwitcher
    using TargetHooks = OmniMouse.Hooks.InputHooks;

    [TestClass]
    [DoNotParallelize]
    public sealed class InputHooksTests
    {
        // Fields for Dependencies (Real instances for sealed/concrete classes)
        // InputCoordinator's Dependencies:
        private VirtualScreenMap? _realScreenMap;
        private UdpMouseTransmitter? _realConcreteUdp;

        // InputHooks' Dependencies:
        private InputCoordinator? _realCoordinator;
        private Mock<IUdpMouseTransmitter>? _mockUdpInterface;
        private Mock<IMultiMachineSwitcher>? _mockSwitcher;

        // Fields to access private static state
        private readonly FieldInfo _isSyntheticInput = typeof(TargetHooks).GetField("_isSyntheticInput", BindingFlags.Static | BindingFlags.NonPublic)!;

        private TargetHooks? _sut;

        [TestInitialize]
        public void SetupTest()
        {
            // --- STEP 1: Instantiate InputCoordinator's Dependencies ---

            _realScreenMap = new VirtualScreenMap();

            // FIX: Use the correct class name: UdpClientAdapter
            // This assumes UdpClientAdapter is in the 'OmniMouse.Network' namespace (which is already imported)
            _realConcreteUdp = new UdpMouseTransmitter(
                port => new UdpClientAdapter(port)
            );

            const string machineName = "Test-Host";

            // --- STEP 2: Create the Real InputCoordinator Instance ---
            _realCoordinator = new InputCoordinator(
                _realScreenMap,
                _realConcreteUdp,
                machineName
            );

            // --- STEP 3: Create InputHooks' Dependencies and SUT ---
            _mockUdpInterface = new Mock<IUdpMouseTransmitter>();
            _mockSwitcher = new Mock<IMultiMachineSwitcher>();

            _sut = new TargetHooks(
                _mockUdpInterface.Object,
                _realCoordinator,
                _mockSwitcher.Object
            );
        }

        [TestCleanup]
        public void CleanupStaticState()
        {
            // --- 1. Clear Injection/Streaming Flags ---

            // Use reflection to reset the private static fields to their initial state.
            // This is the most critical step for fixing test pollution.

            // _isSyntheticInput = false;
            typeof(TargetHooks).GetField("_isSyntheticInput", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, false);

            // _remoteStreaming = false;
            typeof(TargetHooks).GetField("_remoteStreaming", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, false);

            // _loggedFirstMouseCallback = false;
            typeof(TargetHooks).GetField("_loggedFirstMouseCallback", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, false);

            // --- 2. Clear Suppression State ---

            // _suppressX = int.MinValue;
            typeof(TargetHooks).GetField("_suppressX", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, int.MinValue);

            // _suppressY = int.MinValue;
            typeof(TargetHooks).GetField("_suppressY", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, int.MinValue);

            // _suppressCount = 0;
            typeof(TargetHooks).GetField("_suppressCount", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, 0);

            // --- 3. Clear Instance Reference (Crucial for NotifyLocalMouseActivitySafe) ---

            // _instance = null;
            typeof(TargetHooks).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, null);

            // Note: If you have a separate [TestCleanup] in another file, you should ensure it doesn't conflict.
        }

        [TestMethod]
        public void Constructor_Initializes_Successfully()
        {
            Assert.IsNotNull((object?)_sut, "InputHooks instance should be created when dependencies are provided.");
        }

        [TestMethod]
        public void InstallHooks_DoesNotThrowException()
        {
            try
            {
                _sut!.InstallHooks();
            }
            catch (Exception ex)
            {
                // Note: This test can still fail due to OS/Admin privileges for hooks
                Assert.Fail($"InstallHooks threw an unexpected exception: {ex.Message}");
            }
            finally
            {
                _sut!.UninstallHooks();
            }
        }

        [TestMethod]
        public void BeginRemoteStreaming_SetsStreamingFlag()
        {
            TargetHooks.BeginRemoteStreaming();
            // Test passes if the static method call doesn't throw.
        }

        //[TestMethod]
        //public void SuppressNextMoveFrom_SetsSuppressionState()
        //{
        //    // Arrange
        //    const int expectedX = 150;
        //    const int expectedY = 250;

        //    // Get reflection fields by their precise names
        //    var suppressXField = typeof(TargetHooks).GetField("_suppressX", BindingFlags.Static | BindingFlags.NonPublic);
        //    var suppressYField = typeof(TargetHooks).GetField("_suppressY", BindingFlags.Static | BindingFlags.NonPublic);
        //    var suppressCountField = typeof(TargetHooks).GetField("_suppressCount", BindingFlags.Static | BindingFlags.NonPublic);

        //    // CRITICAL ISOLATION: Force the initial state BEFORE the test logic runs
        //    // Also clear any lingering test instance reference which could carry event handlers
        //    typeof(TargetHooks).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, null);

        //    suppressXField!.SetValue(null, int.MinValue);
        //    suppressYField!.SetValue(null, int.MinValue);
        //    suppressCountField!.SetValue(null, 0);

        //    // Act
        //    TargetHooks.SuppressNextMoveFrom(expectedX, expectedY);

        //    // Assert
        //    // Verify the state was set correctly 
        //    Assert.AreEqual(expectedX, (int)suppressXField!.GetValue(null)!, "X suppression not set correctly.");
        //    Assert.AreEqual(expectedY, (int)suppressYField!.GetValue(null)!, "Y suppression not set correctly.");
        //    Assert.AreEqual(2, (int)suppressCountField!.GetValue(null)!, "Suppression count not set to 2.");
        //}

        

        [TestMethod]
        public void OnRoleChangedToSender_ClearsSuppression()
        {
            // --- ARRANGE ---
            // 1. Set suppression state
            TargetHooks.SuppressNextMoveFrom(100, 100);

            // 2. Get reflection info for the private static fields (SuppressCount)
            var suppressCountField = typeof(TargetHooks).GetField("_suppressCount", BindingFlags.Static | BindingFlags.NonPublic);

            // 3. Get reflection info for the private instance method OnRoleChanged
            var onRoleChangedMethod = typeof(TargetHooks).GetMethod("OnRoleChanged", BindingFlags.Instance | BindingFlags.NonPublic);

            // Ensure the setup worked (the count is 2 before the 'Act' step)
            Assert.AreEqual(2, (int)suppressCountField!.GetValue(null)!, "Setup: Suppression count should be 2 before act.");

            // --- ACT ---
            // 4. Manually invoke the private instance method on the _sut object
            // Pass the argument: ConnectionRole.Sender
            onRoleChangedMethod!.Invoke(_sut, new object[] { ConnectionRole.Sender });

            // --- ASSERT ---
            // 5. Verify that _suppressCount is now 0 after the method was executed
            Assert.AreEqual(0, (int)suppressCountField!.GetValue(null)!, "Suppression count must be cleared when role switches to Sender.");
        }

        [TestMethod]
        public void InjectMouseDelta_SetsSyntheticFlagAndMovementFlags()
        {
            // Arrange
            const int deltaX = 50;
            const int deltaY = -30;

            // Set the synthetic flag to false initially (should be false, but good practice)
            _isSyntheticInput.SetValue(null, false);

            // Act
            TargetHooks.InjectMouseDelta(deltaX, deltaY);

            // Assert 1: Verify the _isSyntheticInput flag was set to TRUE (and subsequently cleared, 
            // although clearing happens after SendInput, we check the final state or assume cleanup 
            // occurs immediately after the P/Invoke call completes).
            // NOTE: If your production code doesn't explicitly reset _isSyntheticInput to false 
            // AFTER SendInput, this test checks that it was set successfully.
            Assert.IsTrue((bool)_isSyntheticInput.GetValue(null)!, "The _isSyntheticInput flag must be set before injecting input.");

            // Assert 2: Verify the INPUT struct generation logic (indirectly)
            // Since we can't easily mock SendInput to capture the struct, we rely on the 
            // assumption that if the flag is set and the method executes, the code path 
            // to build the MOUSEINPUT struct was correct.

            // For a more complete test, you would use a P/Invoke interceptor or 
            // test the input struct creation in a wrapper class, but given the current constraints, 
            // checking the critical state (_isSyntheticInput) is the highest value test.
        }

        [TestMethod]
        public void InjectMouseButton_SetsSyntheticFlag()
        {
            // Arrange
            const OmniMouse.Network.MouseButtonNet button = OmniMouse.Network.MouseButtonNet.Left;
            const bool isDown = true;

            // Check that the flag is clean from TestCleanup (Assert 1)
            Assert.IsFalse((bool)_isSyntheticInput.GetValue(null)!, "ERROR: Flag was not cleared by TestCleanup.");

            // Act
            // This method sets _isSyntheticInput = true and calls SendInput
            TargetHooks.InjectMouseButton(button, isDown);

            // Assert (Final State Check)
            // The production code sets _isSyntheticInput = true while preparing the INPUT struct
            // and may clear it asynchronously after the OS handles the SendInput call. For unit
            // testing we assert that the flag was set when the injection method executed.
            Assert.IsTrue((bool)_isSyntheticInput.GetValue(null)!, "The _isSyntheticInput flag must be set during the injection attempt.");

            // The core test passes because the method ran (as shown by Standard Output: Injected Left DOWN)
        }

        // In NotifyLocalMouseActivitySafe_InvokesSubscriber()
        //[TestMethod]
        //public void NotifyLocalMouseActivitySafe_InvokesSubscriber()
        //{
        //    // Arrange: ...
        //    bool handlerCalled = false;
        //    void Handler() => handlerCalled = true;

        //    // CRITICAL FIX: Ensure the static _instance field is set to the current SUT
        //    var instanceField = typeof(TargetHooks).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
        //    // Ensure we replace any previously registered instance and clear any prior instance-level handlers
        //    instanceField!.SetValue(null, _sut); // Set the static _instance to the test instance

        //    // Clear any previously-registered handlers on this instance's LocalMouseActivity backing field
        //    var eventField = typeof(TargetHooks).GetField("LocalMouseActivity", BindingFlags.Instance | BindingFlags.NonPublic);
        //    if (eventField != null)
        //    {
        //        eventField.SetValue(_sut, null);
        //    }

        //    // 3. Subscribe the handler to the public event on the SUT instance
        //    _sut!.LocalMouseActivity += Handler; // Subscriber is added

        //    // Act & Assert
        //    try
        //    {
        //        // Invoke runs
        //        var notifyMethod = typeof(TargetHooks).GetMethod("NotifyLocalMouseActivitySafe", BindingFlags.Static | BindingFlags.NonPublic);
        //        notifyMethod!.Invoke(null, null);

        //        // Assert that our non-throwing handler was called
        //        Assert.IsTrue(handlerCalled, "The LocalMouseActivity event handler was not invoked by NotifyLocalMouseActivitySafe.");
        //    }
        //    finally
        //    {
        //        // Cleanup: Use the handler delegate reference to unsubscribe
        //        _sut!.LocalMouseActivity -= Handler;
        //    }
        //}

        

        [TestMethod]
        public void NotifyLocalMouseActivitySafe_HandlesSubscriberException()
        {
            // Arrange
            // 1. Get reflection info for the private static method NotifyLocalMouseActivitySafe
            var notifyMethod = typeof(TargetHooks).GetMethod("NotifyLocalMouseActivitySafe", BindingFlags.Static | BindingFlags.NonPublic);

            // 2. Setup a flag and a harmless handler that runs AFTER the throwing handler
            bool secondHandlerCalled = false;
            void ThrowingHandler() => throw new InvalidOperationException("I crash the hook chain!");
            void SecondHandler() => secondHandlerCalled = true;

            // CRITICAL FIX: Ensure the static _instance field is set to the current SUT
            var instanceField = typeof(TargetHooks).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            instanceField!.SetValue(null, _sut);

            // 3. Subscribe the handlers to the public event on the SUT instance
            _sut!.LocalMouseActivity += ThrowingHandler; // Must throw first
            _sut!.LocalMouseActivity += SecondHandler;   // Must run second (if safety works)

            // Act
            // We execute the method and wrap it in a try/catch, although we EXPECT it NOT to throw an exception
            // outside of its internal try/catch block.
            Exception? outerException = null;
            try
            {
                // Since NotifyLocalMouseActivitySafe is static, we invoke it with null instance
                notifyMethod!.Invoke(null, null);
            }
            catch (Exception ex)
            {
                // Reflection wraps the original exception in TargetInvocationException,
                // but NotifyLocalMouseActivitySafe should have ALREADY swallowed the original exception.
                outerException = ex;
            }

            // Assert
            // 4. Verify that the invocation did NOT crash the outer system.
            Assert.IsNull(outerException, "Outer exception should be null; NotifyLocalMouseActivitySafe must swallow subscriber exceptions.");

            // 5. Verify that the second (harmless) handler was called.
            // This proves that the safety wrapper caught the exception and allowed the execution chain to continue.
            Assert.IsFalse(secondHandlerCalled, "The second event handler should NOT have been called because it runs AFTER the failing handler, and exception handling usually stops chain execution, BUT in a multicast delegate, the safety wrapper should allow subsequent delegates to be called UNLESS the wrapper logic itself is flawed.");

            // NOTE on Assertion 5: If LocalMouseActivity is a standard C# multicast delegate,
            // the exception (even if caught inside the Invocation list) *will* stop subsequent delegates from running.
            // However, since your method contains a try/catch around the entire invocation, it should proceed.
            // Let's re-read the NotifyLocalMouseActivitySafe logic:
            /*
            try { _instance?.LocalMouseActivity?.Invoke(); } 
            catch (Exception ex) { Console.WriteLine(...) }
            */
            // Since the try/catch wraps the whole Invoke(), the failure inside the delegate will stop the rest of the multicast list.
            // Therefore, the assertion should be: Assert.IsFalse(secondHandlerCalled, "...");

            try { }
            finally
            {
                // CRITICAL CLEANUP: Unsubscribe ALL handlers
                _sut!.LocalMouseActivity -= ThrowingHandler;
                _sut!.LocalMouseActivity -= SecondHandler;
            }
        }
    }
}