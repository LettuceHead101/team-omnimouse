using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.Switching;

namespace NetworkTestProject1.Network
{
    [TestClass]
    public class DefaultMachineLayoutTests
    {
        [TestMethod]
        public void Constructor_Defaults()
        {
            var layout = new DefaultMachineLayout();
            Assert.IsNotNull(layout.Machines);
            Assert.AreEqual(4, layout.Machines.Length);
            foreach (var m in layout.Machines)
                Assert.AreEqual(string.Empty, m);

            Assert.IsTrue(layout.IsOneRow);
            Assert.IsFalse(layout.EnableWrapAround);
        }

        [TestMethod]
        public void Constructor_WithNames_PopulatesAndRespectsFlags()
        {
            var names = new[] { "A", null, "C" };
            var layout = new DefaultMachineLayout(names, oneRow: false, wrapAround: true);

            Assert.AreEqual("A", layout.Machines[0]);
            Assert.AreEqual(string.Empty, layout.Machines[1]);
            Assert.AreEqual("C", layout.Machines[2]);
            Assert.AreEqual(string.Empty, layout.Machines[3]);

            Assert.IsFalse(layout.IsOneRow);
            Assert.IsTrue(layout.EnableWrapAround);
        }

        [TestMethod]
        public void CurrentMachine_SetNullBecomesEmpty()
        {
            var layout = new DefaultMachineLayout();
            layout.CurrentMachine = null!;
            Assert.AreEqual(string.Empty, layout.CurrentMachine);
        }

        [TestMethod]
        public void GetNeighbor_ReturnsNull_ForNullOrWhitespaceCurrent()
        {
            var layout = new DefaultMachineLayout(new[] { "A", "B" });
            Assert.IsNull(layout.GetNeighbor(Direction.Right, null!));
            Assert.IsNull(layout.GetNeighbor(Direction.Left, "   "));
        }

        [TestMethod]
        public void OneRow_VerticalMovement_ReturnsNull()
        {
            var layout = new DefaultMachineLayout(new[] { "A", "B", "C", "D" }, oneRow: true);
            Assert.IsNull(layout.GetNeighbor(Direction.Up, "A"));
            Assert.IsNull(layout.GetNeighbor(Direction.Down, "A"));
        }

        [TestMethod]
        public void OneRow_RightLeft_NoWrap()
        {
            var layout = new DefaultMachineLayout(new[] { "A", "B", "", "D" }, oneRow: true, wrapAround: false);

            Assert.AreEqual("B", layout.GetNeighbor(Direction.Right, "A"));
            Assert.AreEqual("D", layout.GetNeighbor(Direction.Right, "B"));
            Assert.IsNull(layout.GetNeighbor(Direction.Right, "D"));

            Assert.AreEqual("B", layout.GetNeighbor(Direction.Left, "D"));
            Assert.IsNull(layout.GetNeighbor(Direction.Left, "A"));
        }

        [TestMethod]
        public void OneRow_WrapAround_BehavesCircularly()
        {
            var layout = new DefaultMachineLayout(new[] { "A", "B", "", "D" }, oneRow: true, wrapAround: true);

            Assert.AreEqual("A", layout.GetNeighbor(Direction.Right, "D"));
            Assert.AreEqual("D", layout.GetNeighbor(Direction.Left, "A"));
        }

        [TestMethod]
        public void TwoByTwoGrid_Movement_NoWrap()
        {
            var layout = new DefaultMachineLayout(new[] { "A", "B", "C", "D" }, oneRow: false, wrapAround: false);

            // Right
            Assert.AreEqual("B", layout.GetNeighbor(Direction.Right, "A"));
            Assert.AreEqual("D", layout.GetNeighbor(Direction.Right, "C"));

            // Left
            Assert.AreEqual("A", layout.GetNeighbor(Direction.Left, "B"));
            Assert.AreEqual("C", layout.GetNeighbor(Direction.Left, "D"));

            // Up
            Assert.AreEqual("A", layout.GetNeighbor(Direction.Up, "C"));
            Assert.AreEqual("B", layout.GetNeighbor(Direction.Up, "D"));

            // Down
            Assert.AreEqual("C", layout.GetNeighbor(Direction.Down, "A"));
            Assert.AreEqual("D", layout.GetNeighbor(Direction.Down, "B"));
        }

        [TestMethod]
        public void TwoByTwoGrid_WrapAround_BehavesAsExpected()
        {
            var layout = new DefaultMachineLayout(new[] { "A", "B", "C", "D" }, oneRow: false, wrapAround: true);

            // Horizontal wrap
            Assert.AreEqual("A", layout.GetNeighbor(Direction.Right, "B"));
            Assert.AreEqual("C", layout.GetNeighbor(Direction.Left, "D"));

            // vertical wrap
            Assert.AreEqual("C", layout.GetNeighbor(Direction.Up, "A"));
            Assert.AreEqual("A", layout.GetNeighbor(Direction.Down, "C"));
        }

        [TestMethod]
        public void Matching_IsCaseInsensitive_AndTrimmed()
        {
            // Stored names must match after trimming the caller input; the layout
            // implementation does not trim stored names, so provide trimmed names here.
            var layout = new DefaultMachineLayout(new[] { "Alpha", "Beta", string.Empty, string.Empty });
            Assert.AreEqual("Beta", layout.GetNeighbor(Direction.Right, "alpha"));
            Assert.AreEqual("Beta", layout.GetNeighbor(Direction.Right, "  alpha  "));
        }

        [TestMethod]
        public void GetNeighbor_ReturnsNull_WhenSlotEmptyOrNotFound()
        {
            var layout = new DefaultMachineLayout(new[] { "A", "", "", "" }, oneRow: false);
            // In grid mode, right of A is B but B is empty
            Assert.IsNull(layout.GetNeighbor(Direction.Right, "A"));

            // Unknown machine
            Assert.IsNull(layout.GetNeighbor(Direction.Left, "Unknown"));
        }

        [TestMethod]
        public void Constructor_TruncatesExtraNames()
        {
            var names = new[] { "A", "B", "C", "D", "E", "F" };
            var layout = new DefaultMachineLayout(names);
            Assert.AreEqual(4, layout.Machines.Length);
            Assert.AreEqual("A", layout.Machines[0]);
            Assert.AreEqual("B", layout.Machines[1]);
            Assert.AreEqual("C", layout.Machines[2]);
            Assert.AreEqual("D", layout.Machines[3]);
        }

        [TestMethod]
        public void OneRow_SingleMachine_WrapAroundDoesNotReturnSelf()
        {
            var layout = new DefaultMachineLayout(new[] { "A", "", "", "" }, oneRow: true, wrapAround: true);
            Assert.IsNull(layout.GetNeighbor(Direction.Right, "A"));
            Assert.IsNull(layout.GetNeighbor(Direction.Left, "A"));
        }
    }
}
