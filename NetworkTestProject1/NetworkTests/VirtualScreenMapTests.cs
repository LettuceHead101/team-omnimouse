using System;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.Network;

namespace NetworkTestProject1.Network
{
    [TestClass]
    public class VirtualScreenMapTests
    {
        private static MonitorInfo MakeMonitor(string id, string owner, string name, RectInt global, RectInt local, bool primary = false)// we use something similar in HomePageViewModel
        {
            return new MonitorInfo
            {
                MonitorId = id,
                OwnerClientId = owner,
                FriendlyName = name,
                GlobalBounds = global,
                LocalBounds = local,
                IsPrimary = primary
            };
        }

        [TestMethod]
        public void RectInt_Contains_And_Intersects_ToString_Behavior()
        {
            var r = new RectInt(0, 0, 10, 10);
            Assert.IsTrue(r.Contains(0, 0));
            Assert.IsTrue(r.Contains(9, 9));
            Assert.IsFalse(r.Contains(10, 10));
            Assert.AreEqual("[0,0 10x10]", r.ToString());

            var a = new RectInt(0, 0, 5, 5);
            var b = new RectInt(4, 4, 5, 5); // overlaps at (4,4)
            var c = new RectInt(5, 5, 2, 2); // touches at edge -> not intersecting
            Assert.IsTrue(a.Intersects(b));
            Assert.IsFalse(a.Intersects(c));
        }

        [TestMethod]
        public void MonitorInfo_ToString_Includes_Expected_Fields()
        {
            var m = MakeMonitor("mon-1", "client-1", "Friendly", new RectInt(10, 20, 100, 200), new RectInt(0, 0, 100, 200), true);
            var s = m.ToString();
            Assert.IsTrue(s.Contains("Friendly"));
            Assert.IsTrue(s.Contains("Owner=client-1"));
            Assert.IsTrue(s.Contains("Primary=True"));
        }

        [TestMethod]
        public void AddOrUpdateClient_And_RemoveClient_Updates_Snapshots_And_Removes_Monitors()
        {
            var map = new VirtualScreenMap();
            var client = new ClientPc { ClientId = "client-a", FriendlyName = "C A" };
            map.AddOrUpdateClient(client);

            var clientsSnap = map.GetClientsSnapshot();
            Assert.AreEqual(1, clientsSnap.Count);
            Assert.AreEqual("client-a", clientsSnap[0].ClientId);

            // Add a monitor that belongs to that client
            var mon = MakeMonitor("m1", "client-a", "M1", new RectInt(0, 0, 100, 100), new RectInt(0, 0, 100, 100));
            map.AddOrUpdateMonitor(mon);

            // Client MonitorIds should include the monitor
            clientsSnap = map.GetClientsSnapshot();
            Assert.IsTrue(clientsSnap.First(c => c.ClientId == "client-a").MonitorIds.Contains("m1"));

            // remove client should remove associated monitor/s
            var removed = map.RemoveClient("client-a");
            Assert.IsTrue(removed);

            var monitorsSnap = map.GetMonitorsSnapshot();
            Assert.IsFalse(monitorsSnap.Any(m2 => m2.MonitorId == "m1"));
        }

        [TestMethod]
        public void AddOrUpdateMonitor_Overlapping_Throws_InvalidOperationException()
        {
            var map = new VirtualScreenMap();
            var m1 = MakeMonitor("m1", "", "M1", new RectInt(0, 0, 100, 100), new RectInt(0, 0, 100, 100));
            map.AddOrUpdateMonitor(m1);

            var mOverlap = MakeMonitor("m2", "", "M2", new RectInt(50, 50, 100, 100), new RectInt(0, 0, 100, 100));
            Assert.ThrowsException<InvalidOperationException>(() => map.AddOrUpdateMonitor(mOverlap));
        }

        [TestMethod]
        public void CanPlaceMonitor_Excludes_Specified_MonitorId_For_Update()
        {
            var map = new VirtualScreenMap();
            var m1 = MakeMonitor("m1", "", "M1", new RectInt(0, 0, 100, 100), new RectInt(0, 0, 100, 100));
            map.AddOrUpdateMonitor(m1);

            // Proposed bounds overlapping m1
            var proposed = new RectInt(10, 10, 20, 20);
            Assert.IsFalse(map.CanPlaceMonitor(proposed));

            // But if we exclude m1 (updating it), should be allowed
            Assert.IsTrue(map.CanPlaceMonitor(proposed, excludeMonitorId: "m1"));
        }

        [TestMethod]
        public void TranslateGlobalToLocal_And_TranslateLocalToGlobal_Work_As_Expected()
        {
            var map = new VirtualScreenMap();
            var mon = MakeMonitor("monA", "", "A", new RectInt(100, 50, 200, 100), new RectInt(0, 0, 200, 100));
            map.AddOrUpdateMonitor(mon);

            var ok = map.TranslateGlobalToLocal(150, 75, out var found, out var localX, out var localY);
            Assert.IsTrue(ok);
            Assert.IsNotNull(found);
            Assert.AreEqual("monA", found!.MonitorId);
            Assert.AreEqual(50, localX);
            Assert.AreEqual(25, localY);

            var got = map.TranslateLocalToGlobal("monA", 10, 20, out var gX, out var gY);
            Assert.IsTrue(got);
            Assert.AreEqual(110, gX);
            Assert.AreEqual(70, gY);

            // Unknown monitor id
            Assert.IsFalse(map.TranslateLocalToGlobal("nope", 1, 1, out _, out _));
        }

        [TestMethod]
        public void FindMonitorAt_Returns_Null_When_No_Monitor_Present()
        {
            var map = new VirtualScreenMap();
            Assert.IsNull(map.FindMonitorAt(0, 0));
        }

        [TestMethod]
        public void RemoveMonitor_Removes_From_Clients_And_Snapshots()
        {
            var map = new VirtualScreenMap();
            var client = new ClientPc { ClientId = "client1" };
            map.AddOrUpdateClient(client);

            var mon = MakeMonitor("m1", "client1", "M1", new RectInt(0, 0, 100, 100), new RectInt(0, 0, 100, 100));
            map.AddOrUpdateMonitor(mon);

            Assert.IsTrue(map.RemoveMonitor("m1"));

            // Ensure client no longer references it
            var clients = map.GetClientsSnapshot();
            Assert.IsFalse(clients.First(c => c.ClientId == "client1").MonitorIds.Contains("m1"));

            // Remove non-existent monitor returns false
            Assert.IsFalse(map.RemoveMonitor("not-there"));
        }

        [TestMethod]
        public void FindNeighbor_Finds_Closest_In_Direction()
        {
            var map = new VirtualScreenMap();

            var subject = MakeMonitor("center", "", "C", new RectInt(100, 100, 100, 100), new RectInt(0, 0, 100, 100));
            var left = MakeMonitor("left", "", "L", new RectInt(0, 120, 100, 60), new RectInt(0, 0, 100, 60));
            var right = MakeMonitor("right", "", "R", new RectInt(200, 110, 100, 80), new RectInt(0, 0, 100, 80));
            var up = MakeMonitor("up", "", "U", new RectInt(110, 0, 80, 100), new RectInt(0, 0, 80, 100));
            var down = MakeMonitor("down", "", "D", new RectInt(120, 200, 60, 100), new RectInt(0, 0, 60, 100));

            map.AddOrUpdateMonitor(subject);
            map.AddOrUpdateMonitor(left);
            map.AddOrUpdateMonitor(right);
            map.AddOrUpdateMonitor(up);
            map.AddOrUpdateMonitor(down);

            Assert.AreEqual("right", map.FindNeighbor(subject, "right")?.MonitorId);
            Assert.AreEqual("left", map.FindNeighbor(subject, "left")?.MonitorId);
            Assert.AreEqual("up", map.FindNeighbor(subject, "up")?.MonitorId);
            Assert.AreEqual("down", map.FindNeighbor(subject, "down")?.MonitorId);
        }

        [TestMethod]
        public void ReplaceSpatialIndex_Calls_Rebuild_On_New_Index()
        {
            var map = new VirtualScreenMap();
            var called = false;

            var fake = new FakeIndex(() => called = true);
            map.ReplaceSpatialIndex(fake);

            // After replace, ReplaceSpatialIndex invokes Rebuild synchronously (under lock)
            Assert.IsTrue(called);
        }

        [TestMethod]
        public void RebuildIndex_Invokes_LayoutChanged_Event_Synchronously()
        {
            var map = new VirtualScreenMap();
            var called = 0;
            map.LayoutChanged += () => Interlocked.Increment(ref called);

            // RebuildIndex invokes LayoutChanged outside of lock synchronously
            map.RebuildIndex();

            Assert.AreEqual(1, called);
        }

        // A simple fake ISpatialIndex used to verify ReplaceSpatialIndex behavior.
        private sealed class FakeIndex : ISpatialIndex
        {
            private readonly Action _onRebuild;
            public FakeIndex(Action onRebuild) => _onRebuild = onRebuild;

            public void Rebuild(System.Collections.Generic.IEnumerable<MonitorInfo> monitors)
            {
                _onRebuild();
            }

            public System.Collections.Generic.IEnumerable<MonitorInfo> QueryPoint(int x, int y)
            {
                yield break;
            }
        }
    }
}