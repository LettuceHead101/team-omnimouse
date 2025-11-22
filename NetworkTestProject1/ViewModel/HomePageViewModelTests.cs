using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.ViewModel;
using OmniMouse.Network;
using System;
using System.Reflection;
using System.Windows.Forms;
using System.Linq;

namespace NetworkTestProject1.ViewModel
{
    [TestClass]
    [DoNotParallelize] // Uses WPF-related types indirectly; avoid parallel interference
    public class HomePageViewModelTests
    {
        private static T? GetPrivateField<T>(object instance, string fieldName) where T : class
        {
            var fi = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            return fi?.GetValue(instance) as T;
        }

        [TestMethod]
        public void ConsoleOutput_Property_UsesSetPropertyBehavior()
        {
            var vm = new HomePageViewModel();
            int changed = 0;
            string? lastName = null;
            vm.PropertyChanged += (s, e) => { changed++; lastName = e.PropertyName; };

            vm.ConsoleOutput = "first";
            Assert.AreEqual("first", vm.ConsoleOutput);
            Assert.AreEqual(1, changed);
            Assert.AreEqual("ConsoleOutput", lastName);

            vm.ConsoleOutput = "first";
            Assert.AreEqual(1, changed);
        }

        [TestMethod]
        public void Commands_Exist()
        {
            var vm = new HomePageViewModel();
            Assert.IsNotNull(vm.StartAsSourceCommand);
            Assert.IsNotNull(vm.StartAsReceiverCommand);
            Assert.IsNotNull(vm.DisconnectCommand);
        }

        [TestMethod]
        public void ExecuteStartAsSource_SetsExpectedState_AndCreatesUdp()
        {
            var vm = new HomePageViewModel();

            vm.StartAsSourceCommand.Execute(null);

            Assert.IsTrue(vm.IsConnected);
            Assert.IsFalse(vm.IsPeerConfirmed);
            Assert.IsFalse(vm.IsMouseSource);

            var udp = GetPrivateField<UdpMouseTransmitter>(vm, "_udp");
            Assert.IsNotNull(udp);

            vm.DisconnectCommand.Execute(null);
            Assert.IsFalse(vm.IsConnected);
        }

        [TestMethod]
        public void ExecuteStartAsReceiver_SetsExpectedState_AndCreatesUdp()
        {
            var vm = new HomePageViewModel();
            vm.HostIp = string.Empty;

            vm.StartAsReceiverCommand.Execute(null);

            Assert.IsTrue(vm.IsConnected);
            Assert.IsFalse(vm.IsPeerConfirmed);
            Assert.IsFalse(vm.IsMouseSource);

            var udp = GetPrivateField<UdpMouseTransmitter>(vm, "_udp");
            Assert.IsNotNull(udp);

            vm.DisconnectCommand.Execute(null);
            Assert.IsFalse(vm.IsConnected);
        }

        [TestMethod]
        public void ExecuteDisconnect_ResetsState_AndReleasesUdp()
        {
            var vm = new HomePageViewModel();
            vm.StartAsSourceCommand.Execute(null);
            var udpBefore = GetPrivateField<UdpMouseTransmitter>(vm, "_udp");
            Assert.IsNotNull(udpBefore);

            vm.DisconnectCommand.Execute(null);

            Assert.IsFalse(vm.IsConnected);
            Assert.IsFalse(vm.IsPeerConfirmed);
            Assert.IsFalse(vm.IsMouseSource);

            var udpAfter = GetPrivateField<UdpMouseTransmitter>(vm, "_udp");
            Assert.IsNull(udpAfter);
        }

        [TestMethod]
        public void PopulateLocalMonitors_AddsMonitorForEachPhysicalScreen()
        {
            // Arrange
            var vm = new HomePageViewModel();
            var map = new VirtualScreenMap();
            var clientId = Guid.NewGuid().ToString();

            var populateMethod = typeof(HomePageViewModel)
                .GetMethod("PopulateLocalMonitors", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(populateMethod, "PopulateLocalMonitors must exist (private).");

            var screens = Screen.AllScreens;
            Assert.IsTrue(screens.Length > 0, "Test requires at least one display.");

            // Act (invoke private method via reflection)
            populateMethod!.Invoke(vm, new object[] { map, clientId });

            // Assert
            var monitors = map.GetMonitorsSnapshot();
            // Filter by OwnerClientId to isolate what this call added
            var owned = monitors.Where(m => m.OwnerClientId == clientId).ToList();
            Assert.AreEqual(screens.Length, owned.Count, "Should create one monitor per physical screen.");

            foreach (var screen in screens)
            {
                // Use center point to locate monitor reliably
                int centerX = screen.Bounds.X + screen.Bounds.Width / 2;
                int centerY = screen.Bounds.Y + screen.Bounds.Height / 2;

                var monitor = map.FindMonitorAt(centerX, centerY);
                Assert.IsNotNull(monitor, $"No monitor found covering center of {screen.DeviceName}.");

                Assert.AreEqual(clientId, monitor!.OwnerClientId, "OwnerClientId mismatch.");
                Assert.AreEqual(screen.DeviceName, monitor.FriendlyName, "FriendlyName mismatch.");
                Assert.AreEqual(screen.Primary, monitor.IsPrimary, "Primary flag mismatch.");

                // Global bounds
                Assert.AreEqual(screen.Bounds.X, monitor.GlobalBounds.X);
                Assert.AreEqual(screen.Bounds.Y, monitor.GlobalBounds.Y);
                Assert.AreEqual(screen.Bounds.Width, monitor.GlobalBounds.Width);
                Assert.AreEqual(screen.Bounds.Height, monitor.GlobalBounds.Height);

                // Local bounds always origin-based
                Assert.AreEqual(0, monitor.LocalBounds.X);
                Assert.AreEqual(0, monitor.LocalBounds.Y);
                Assert.AreEqual(screen.Bounds.Width, monitor.LocalBounds.Width);
                Assert.AreEqual(screen.Bounds.Height, monitor.LocalBounds.Height);
            }
        }
    }
}
