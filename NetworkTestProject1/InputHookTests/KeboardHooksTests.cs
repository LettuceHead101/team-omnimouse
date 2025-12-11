using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OmniMouse.Network;
using OmniMouse.Switching;

// Alias the class to avoid confusion
using TargetHooks = OmniMouse.Hooks.InputHooks;

namespace NetworkTestProject1.Hooks
{
    [TestClass]
    public class KeyboardTests
    {
        // Static lock to synchronize test execution and prevent interference
        private static readonly object _testLock = new object();
        
        private Mock<IUdpMouseTransmitter>? _mockUdpBase;
        private Mock<IUdpKeyboardTransmitter>? _mockKbTransmitter;
        private InputCoordinator? _dummyCoordinator;
        private TargetHooks? _hooks;

        // Win32 Constants
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int VK_A = 0x41;

        // Ensure strictly sequential layout to match Windows API
        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [TestInitialize]
        public void Setup()
        {
            // CRITICAL: Reset all static state first to ensure test isolation
            SetPrivateStaticField("_instance", null!);
            SetPrivateStaticField("_remoteStreaming", false);
            SetPrivateStaticField("_isSyntheticKeyboard", false);

            // 1. Setup Mock Logic
            _mockUdpBase = new Mock<IUdpMouseTransmitter>();
            
            // CRITICAL: Ensure the mock implements the second interface
            _mockKbTransmitter = _mockUdpBase.As<IUdpKeyboardTransmitter>();

            // 2. Create Zombie Coordinator
            _dummyCoordinator = (InputCoordinator)RuntimeHelpers.GetUninitializedObject(typeof(InputCoordinator));

            // 3. Initialize Hooks
            // Verify that the object passed actually IS a KeyboardTransmitter before passing it
            var transmitterObj = _mockUdpBase.Object;
            if (!(transmitterObj is IUdpKeyboardTransmitter))
            {
                throw new Exception("Mock Setup Failed: Object does not implement IUdpKeyboardTransmitter");
            }

            // The constructor will automatically set _instance = this and _udpTransmitter = transmitterObj
            _hooks = new TargetHooks(transmitterObj, _dummyCoordinator, null);

            // FORCE set _instance to _hooks again, in case another test's constructor ran after our null assignment
            SetPrivateStaticField("_instance", _hooks);

            // Verify that _instance is now the same object as _hooks
            var instance = typeof(TargetHooks).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
            if (!ReferenceEquals(instance, _hooks))
            {
                throw new Exception($"Test setup validation failed: _instance is not the same object as _hooks after forced assignment. _instance hash={instance?.GetHashCode()}, _hooks hash={_hooks?.GetHashCode()}");
            }
        }

        [TestCleanup]
        public void Cleanup()
        {
            _hooks?.UninstallHooks();
            
            // Force clear all static state to ensure test isolation
            SetPrivateStaticField("_instance", null!);
            SetPrivateStaticField("_remoteStreaming", false);
            SetPrivateStaticField("_isSyntheticKeyboard", false);
        }

        [TestMethod]
        [Priority(1)]
        public void KeyboardHook_WhenStreaming_ForwardsKeyAndBlocksInput()
        {
            // Clear mock invocations from any previous tests
            _mockUdpBase!.Invocations.Clear();
            
            // Arrange
            var hookStruct = new KBDLLHOOKSTRUCT
            {
                vkCode = VK_A,
                scanCode = 0x1E,
                flags = 0,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            };

            IntPtr lParam = MarshalAllocStruct(hookStruct);

            try
            {
                // Lock to prevent interference from other tests manipulating static state
                lock (_testLock)
                {
                    // Set state RIGHT BEFORE invoking callback to minimize interference window
                    SetPrivateStaticField("_remoteStreaming", true);
                    SetPrivateStaticField("_instance", _hooks);
                    
                    // Act
                    IntPtr result = InvokeKbHookCallback(WM_KEYDOWN, lParam);

                    // Assert
                    Assert.AreEqual((IntPtr)1, result, "Should return 1 to block input when streaming.");

                    // Verify the method was called
                    _mockKbTransmitter!.Verify(x => x.SendKeyboard(
                        VK_A, 
                        0x1E, 
                        true, // isDown
                        0     // flags
                    ), Times.Once);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(lParam);
            }
        }

        [TestMethod]
        [Priority(2)]
        public void KeyboardHook_WhenStreaming_ForwardsKeyUp()
        {
            // Clear mock invocations from any previous tests
            _mockUdpBase!.Invocations.Clear();
            
            // Arrange
            var hookStruct = new KBDLLHOOKSTRUCT
            {
                vkCode = VK_A,
                scanCode = 0x1E,
                flags = 0,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            };
            IntPtr lParam = MarshalAllocStruct(hookStruct);

            try
            {
                // Lock to prevent interference from other tests manipulating static state
                lock (_testLock)
                {
                    // Set state RIGHT BEFORE invoking callback to minimize interference window
                    SetPrivateStaticField("_remoteStreaming", true);
                    SetPrivateStaticField("_instance", _hooks);
                    
                    // Act
                    IntPtr result = InvokeKbHookCallback(WM_KEYUP, lParam);

                    // Assert
                    Assert.AreEqual((IntPtr)1, result);
                    _mockKbTransmitter!.Verify(x => x.SendKeyboard(VK_A, 0x1E, false, 0), Times.Once);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(lParam);
            }
        }

        [TestMethod]
        [Priority(3)]
        public void KeyboardHook_WhenNotStreaming_DoesNotForward()
        {
            // Clear mock invocations from any previous tests
            _mockUdpBase!.Invocations.Clear();
            
            // Arrange
            var hookStruct = new KBDLLHOOKSTRUCT { vkCode = VK_A };
            IntPtr lParam = MarshalAllocStruct(hookStruct);

            try
            {
                // Lock to prevent interference from other tests manipulating static state
                lock (_testLock)
                {
                    // Set state RIGHT BEFORE invoking callback to minimize interference window
                    SetPrivateStaticField("_remoteStreaming", false);
                    SetPrivateStaticField("_instance", _hooks);
                    
                    // Act
                    InvokeKbHookCallback(WM_KEYDOWN, lParam);

                    // Assert
                    _mockKbTransmitter!.Verify(x => x.SendKeyboard(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<int>()), Times.Never);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(lParam);
            }
        }

        [TestMethod]
        [Priority(4)]
        public void KeyboardHook_IgnoresSyntheticInjection_ToPreventLoops()
        {
            // Arrange
            SetPrivateStaticField("_isSyntheticKeyboard", true); 

            var hookStruct = new KBDLLHOOKSTRUCT { vkCode = VK_A };
            IntPtr lParam = MarshalAllocStruct(hookStruct);

            try
            {
                // Act
                InvokeKbHookCallback(WM_KEYDOWN, lParam);

                // Assert
                _mockKbTransmitter!.Verify(x => x.SendKeyboard(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<int>()), Times.Never);

                bool flagState = GetPrivateStaticBool("_isSyntheticKeyboard");
                Assert.IsFalse(flagState, "Hook should reset the synthetic flag after processing");
            }
            finally
            {
                Marshal.FreeHGlobal(lParam);
            }
        }

        [TestMethod]
        [Priority(5)]
        public void InjectKeyboard_KeyDown_SetsAndResetsSyntheticFlag()
        {
            // Clear invocations
            _mockUdpBase!.Invocations.Clear();

            int obsVk = 0, obsScan = 0, obsCalls = 0; bool obsIsDown = false; int obsFlags = -1;
            TargetHooks.InjectKeyboardObserver = (vk, scan, isDown, flags) =>
            {
                obsVk = vk; obsScan = scan; obsIsDown = isDown; obsFlags = flags; obsCalls++;
            };

            // Act: inject key down
            TargetHooks.InjectKeyboard(VK_A, 0x1E, true);

            // Assert pre-callback: synthetic flag should be set
            Assert.IsTrue(GetPrivateStaticBool("_isSyntheticKeyboard"), "Synthetic flag should be set immediately after InjectKeyboard.");
            Assert.AreEqual(1, obsCalls, "Observer should have been called exactly once.");
            Assert.AreEqual(VK_A, obsVk); Assert.AreEqual(0x1E, obsScan); Assert.IsTrue(obsIsDown); Assert.AreEqual(0, obsFlags, "Down event flags should be 0.");

            // Prepare hook struct for callback (values match injection)
            var hookStruct = new KBDLLHOOKSTRUCT { vkCode = VK_A, scanCode = 0x1E, flags = 0 };
            IntPtr lParam = MarshalAllocStruct(hookStruct);
            try
            {
                // Invoke hook callback – should bypass and reset synthetic flag
                InvokeKbHookCallback(WM_KEYDOWN, lParam);
                Assert.IsFalse(GetPrivateStaticBool("_isSyntheticKeyboard"), "Synthetic flag should be cleared after callback bypass.");
                // Verify no forwarding occurred
                _mockKbTransmitter!.Verify(x => x.SendKeyboard(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<int>()), Times.Never);
            }
            finally { Marshal.FreeHGlobal(lParam); }
        }

        [TestMethod]
        [Priority(6)]
        public void InjectKeyboard_KeyUp_SetsKeyUpFlagAndResets()
        {
            // Clear invocations
            _mockUdpBase!.Invocations.Clear();

            int obsVk = 0, obsScan = 0, obsCalls = 0; bool obsIsDown = true; int obsFlags = -1;
            TargetHooks.InjectKeyboardObserver = (vk, scan, isDown, flags) =>
            {
                obsVk = vk; obsScan = scan; obsIsDown = isDown; obsFlags = flags; obsCalls++;
            };

            // Act: inject key up
            TargetHooks.InjectKeyboard(VK_A, 0x1E, false);

            // Assert pre-callback
            Assert.IsTrue(GetPrivateStaticBool("_isSyntheticKeyboard"), "Synthetic flag should be set for key up injection.");
            Assert.AreEqual(1, obsCalls);
            Assert.AreEqual(VK_A, obsVk); Assert.AreEqual(0x1E, obsScan); Assert.IsFalse(obsIsDown);
            Assert.IsTrue(obsFlags != 0, "Up event should have non-zero KEYEVENTF_KEYUP flag.");

            var hookStruct = new KBDLLHOOKSTRUCT { vkCode = VK_A, scanCode = 0x1E, flags = 0 };
            IntPtr lParam = MarshalAllocStruct(hookStruct);
            try
            {
                InvokeKbHookCallback(WM_KEYUP, lParam);
                Assert.IsFalse(GetPrivateStaticBool("_isSyntheticKeyboard"), "Synthetic flag should reset after key up bypass.");
                _mockKbTransmitter!.Verify(x => x.SendKeyboard(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<int>()), Times.Never);
            }
            finally { Marshal.FreeHGlobal(lParam); }
        }

        #region Helpers

        private void SetPrivateStaticField(string fieldName, object value)
        {
            var field = typeof(TargetHooks).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null) throw new ArgumentException($"Field '{fieldName}' not found on InputHooks class.");
            field.SetValue(null, value);
        }

        private bool GetPrivateStaticBool(string fieldName)
        {
            var field = typeof(TargetHooks).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null) throw new ArgumentException($"Field '{fieldName}' not found");
            return (bool)field.GetValue(null)!;
        }

        private IntPtr InvokeKbHookCallback(int wParam, IntPtr lParam)
        {
            var method = typeof(TargetHooks).GetMethod("KbHookCallback", BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null) throw new ArgumentException("Method KbHookCallback not found");
            return (IntPtr)method.Invoke(null, new object[] { 0, (IntPtr)wParam, lParam })!;
        }

        private IntPtr MarshalAllocStruct<T>(T structure) where T : struct
        {
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(structure));
            Marshal.StructureToPtr(structure, ptr, false);
            return ptr;
        }

        #endregion
    }
}