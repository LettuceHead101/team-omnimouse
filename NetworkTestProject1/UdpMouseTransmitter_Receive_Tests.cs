using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.Network;

namespace NetworkTestProject1
{
    [TestClass]
    public sealed class UdpMouseTransmitterReceiveTests
    {
        private sealed class FakeUdpClient : IUdpClient
        {
            private readonly Queue<byte[]> _queue = new();
            private readonly Socket _socket;
            public volatile bool EnableReceive = true;

            public FakeUdpClient()
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            }

            public Socket Client => _socket;

            public void Enqueue(byte[] data)
            {
                lock (_queue) { _queue.Enqueue(data); }
            }

            public int Send(byte[] dgram, int bytes, IPEndPoint? endPoint) => bytes;

            public byte[] Receive(ref IPEndPoint remoteEP)
            {
                // Busy-wait with small sleeps to simulate blocking receive
                while (EnableReceive)
                {
                    lock (_queue)
                    {
                        if (_queue.Count > 0)
                        {
                            remoteEP = new IPEndPoint(IPAddress.Loopback, 12345);
                            return _queue.Dequeue();
                        }
                    }
                    Thread.Sleep(2);
                }
                return Array.Empty<byte>();
            }

            public void Close() => EnableReceive = false;

            public void Dispose()
            {
                try { _socket.Close(); } catch { }
                _socket.Dispose();
            }
        }

        [TestMethod]
        [Timeout(3000)]
        public void Receive_NormalizedPacket_MapsAndInvokesCursorSetter()
        {
            var fake = new FakeUdpClient();

            int gotX = int.MinValue, gotY = int.MinValue;
            using var ready = new ManualResetEventSlim(false);

            var tx = new UdpMouseTransmitter(() => fake, _ => fake, (x, y) =>
            {
                gotX = x; gotY = y;
                ready.Set();
            });

            tx.StartHost();

            var buf = new byte[1 + 8];
            buf[0] = 0x01;
            Array.Copy(BitConverter.GetBytes(0.5f), 0, buf, 1, 4);
            Array.Copy(BitConverter.GetBytes(0.25f), 0, buf, 1 + 4, 4);
            fake.Enqueue(buf);

            Assert.IsTrue(ready.Wait(1000), "Receive loop did not invoke cursor setter.");

            CoordinateNormalizer.GetVirtualScreenBounds(out var left, out var top, out var width, out var height);
            Assert.IsTrue(gotX >= left && gotX < left + width, "Mapped X is out of virtual bounds.");
            Assert.IsTrue(gotY >= top && gotY < top + height, "Mapped Y is out of virtual bounds.");

            fake.EnableReceive = false;
            tx.Disconnect();
            fake.Dispose();
        }
    }
}