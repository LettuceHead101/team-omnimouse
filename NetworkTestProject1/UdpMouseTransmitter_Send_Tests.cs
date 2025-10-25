using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OmniMouse.Network;

namespace NetworkTestProject1
{
    [TestClass]
    public sealed class UdpMouseTransmitterSendTests
    {
        [TestMethod]
        public void SendNormalizedMousePosition_WhenCoHost_SendsMsgType01WithFloats()
        {
            var mock = new Mock<IUdpClient>();
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            mock.SetupGet(x => x.Client).Returns(socket);

            byte[]? sent = null;
            IPEndPoint? ep = null;

            mock.Setup(m => m.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Callback<byte[], int, IPEndPoint>((buf, bytes, endpoint) =>
                {
                    sent = buf.Take(bytes).ToArray();
                    ep = endpoint;
                })
                .Returns<int>(bytes => bytes);

            var tx = new UdpMouseTransmitter(() => mock.Object, _ => mock.Object, (x, y) => { });
            tx.StartCoHost("127.0.0.1");
            tx.SendNormalizedMousePosition(0.25f, 0.75f);

            Assert.IsNotNull(sent, "No packet was sent.");
            Assert.AreEqual(1 + 8, sent!.Length, "Unexpected packet length.");
            Assert.AreEqual(0x01, sent![0], "Unexpected message type.");

            var nx = BitConverter.ToSingle(sent!, 1);
            var ny = BitConverter.ToSingle(sent!, 1 + 4);

            Assert.AreEqual(0.25f, nx, 1e-6f, "Unexpected normalized X.");
            Assert.AreEqual(0.75f, ny, 1e-6f, "Unexpected normalized Y.");
            Assert.IsNotNull(ep, "No endpoint was used for send.");
            tx.Disconnect();
            socket.Dispose();
        }
    }
}