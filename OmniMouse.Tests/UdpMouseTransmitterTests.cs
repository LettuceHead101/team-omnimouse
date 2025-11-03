using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OmniMouse.Network;
using System.Net;
using System.Net.Sockets;
using System.Linq;

namespace OmniMouse.Tests
{
    [TestClass]
    public class UdpMouseTransmitterTests
    {
        [TestMethod]
        public void SendNormalizedMousePosition_WhenCoHost_SendsMsgType01WithFloats()
        {
            var mockUdp = new Mock<IUdpClient>();
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            mockUdp.SetupGet(c => c.Client).Returns(socket);

            byte[]? sent = null;
            IPEndPoint? ep = null;

            mockUdp.Setup(m => m.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Callback<byte[], int, IPEndPoint>((buf, bytes, endpoint) =>
                {
                    sent = buf.Take(bytes).ToArray();
                    ep = endpoint;
                })
                .Returns<int>(bytes => bytes);

            var tx = new UdpMouseTransmitter(() => mockUdp.Object, _ => mockUdp.Object, (x, y) => { });
            tx.StartCoHost("127.0.0.1");
            tx.SendNormalizedMousePosition(0.25f, 0.75f);

            Assert.IsNotNull(sent, "No packet was sent.");
            Assert.AreEqual(1 + 8, sent!.Length, "Unexpected packet length.");
            Assert.AreEqual(0x01, sent![0], "Unexpected message type.");

            var nx = System.BitConverter.ToSingle(sent!, 1);
            var ny = System.BitConverter.ToSingle(sent!, 1 + 4);

            Assert.AreEqual(0.25f, nx, 1e-6f, "Unexpected normalized X.");
            Assert.AreEqual(0.75f, ny, 1e-6f, "Unexpected normalized Y.");
            Assert.IsNotNull(ep, "No endpoint was used for send.");
            tx.Disconnect();
            socket.Dispose();
        }

        [TestMethod]
        public void SendNormalizedMousePosition_WithoutRemote_DoesNotSend()
        {
            var mockUdp = new Mock<IUdpClient>();
            var tx = new UdpMouseTransmitter(() => mockUdp.Object, _ => mockUdp.Object, (x, y) => { });

            // Do not call StartHost/StartCoHost/StartPeer -> no remote endpoint
            tx.SendNormalizedMousePosition(0.1f, 0.2f);

            mockUdp.Verify(c => c.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()), Times.Never);
        }

        [TestMethod]
        public void Disconnect_ClosesUdpClient()
        {
            var mockUdp = new Mock<IUdpClient>();
            var tx = new UdpMouseTransmitter(() => mockUdp.Object, _ => mockUdp.Object, (x, y) => { });
            tx.StartCoHost("127.0.0.1");

            tx.Disconnect();

            mockUdp.Verify(c => c.Close(), Times.Once);
        }
    }
}