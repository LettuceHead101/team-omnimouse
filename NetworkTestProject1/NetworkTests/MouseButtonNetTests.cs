using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.Network;

namespace NetworkTestProject1.Network
{
    [TestClass]
    public class MouseButtonNetTests
    {
        [TestMethod]
        public void EnumValues_MatchExpectedWireFormat()
        {
            // Assert that the byte values of the enum members match the protocol specification.
            // This prevents accidental changes that would break network compatibility.
            Assert.AreEqual(1, (byte)MouseButtonNet.Left, "Left button value should be 1.");
            Assert.AreEqual(2, (byte)MouseButtonNet.Right, "Right button value should be 2.");
            Assert.AreEqual(3, (byte)MouseButtonNet.Middle, "Middle button value should be 3.");
        }

        [TestMethod]
        public void EnumUnderlyingType_IsByte()
        {
            // Assert that the underlying type is 'byte' for efficient network transmission.
            Assert.AreEqual(typeof(byte), System.Enum.GetUnderlyingType(typeof(MouseButtonNet)), "The underlying type of MouseButtonNet should be byte.");
        }
    }
}