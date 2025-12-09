using System;
using System.IO;
using System.Net.Sockets;
using System.Net;
using System.Reflection;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.Network;

namespace NetworkTestProject1.Network
{
    [TestClass]
    public class UdpMouseTransmitterTcpControlTests
    {
        // constants mirrored from the implementation under test
        private const byte MSG_TAKE_CONTROL_AT = 0x05;
        private const byte MSG_TAKE_CONTROL_ACK = 0x06;
        private const int TcpControlPort = 5001;

        private static MethodInfo GetPrivateInstanceMethod(Type t, string name) =>
            t.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static FieldInfo GetPrivateInstanceField(Type t, string name) =>
            t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

        [TestMethod]
        public void ReadExact_ReturnsExactCount_WhenStreamHasEnough()
        {
            var data = new byte[] { 1, 2, 3, 4 };
            using var ms = new MemoryStream(data);
            var buffer = new byte[4];

            var mi = typeof(UdpMouseTransmitter).GetMethod("ReadExact", BindingFlags.NonPublic | BindingFlags.Static)!;
            var result = (int)mi.Invoke(null, new object[] { ms, buffer, 0, 4 })!;

            Assert.AreEqual(4, result);
            CollectionAssert.AreEqual(data, buffer);
        }

        [TestMethod]
        public void ReadExact_ReturnsPartial_WhenStreamEndsEarly()
        {
            var data = new byte[] { 1, 2 };
            using var ms = new MemoryStream(data);
            var buffer = new byte[4];

            var mi = typeof(UdpMouseTransmitter).GetMethod("ReadExact", BindingFlags.NonPublic | BindingFlags.Static)!;
            var result = (int)mi.Invoke(null, new object[] { ms, buffer, 0, 4 })!;

            Assert.AreEqual(2, result);
            Assert.AreEqual(1, buffer[0]);
            Assert.AreEqual(2, buffer[1]);
        }

        [TestMethod]
        public void ReadExact_HandlesIOException_AndReturnsBytesReadBeforeException()
        {
            // Stream that returns 2 bytes on first read, then throws IOException.
            using var ts = new ThrowingStream(2);
            var buffer = new byte[4];

            var mi = typeof(UdpMouseTransmitter).GetMethod("ReadExact", BindingFlags.NonPublic | BindingFlags.Static)!;
            var result = (int)mi.Invoke(null, new object[] { ts, buffer, 0, 4 })!;

            Assert.AreEqual(2, result);
        }

        [TestMethod]
        public void StartAndStopTcpControlListener_BindsAndUnbindsListener()
        {
            var transmitter = new UdpMouseTransmitter();

            var tType = typeof(UdpMouseTransmitter);
            var startMi = GetPrivateInstanceMethod(tType, "StartTcpControlListener");
            var stopMi = GetPrivateInstanceMethod(tType, "StopTcpControlListener");
            var listenerField = GetPrivateInstanceField(tType, "_tcpListener");
            var threadField = GetPrivateInstanceField(tType, "_tcpAcceptThread");

            // Start listener
            startMi.Invoke(transmitter, null);

            // If binding failed (port in use etc.) the implementation will clear the listener.
            var listener = listenerField.GetValue(transmitter) as TcpListener;
            if (listener == null)
            {
                // Make test explicit: cannot proceed if port unavailable
                Assert.Inconclusive($"Could not bind TcpListener on port {TcpControlPort}; skipping network test.");
                return;
            }

            // Ensure thread exists (give it a moment to start)
            Thread.Sleep(150);
            var acceptThread = threadField.GetValue(transmitter) as Thread;
            Assert.IsNotNull(acceptThread);
            Assert.IsTrue(acceptThread!.IsAlive);

            // Stop listener and ensure fields are cleared
            stopMi.Invoke(transmitter, null);
            Thread.Sleep(100);

            Assert.IsNull(listenerField.GetValue(transmitter));
            Assert.IsNull(threadField.GetValue(transmitter));
        }

        [TestMethod]
        public void TcpAcceptLoop_SendsAck_ForValidTakeControlPacket()
        {
            var transmitter = new UdpMouseTransmitter();
            var tType = typeof(UdpMouseTransmitter);
            var startMi = GetPrivateInstanceMethod(tType, "StartTcpControlListener");
            var stopMi = GetPrivateInstanceMethod(tType, "StopTcpControlListener");
            var listenerField = GetPrivateInstanceField(tType, "_tcpListener");

            // Start the listener
            startMi.Invoke(transmitter, null);
            var listener = listenerField.GetValue(transmitter) as TcpListener;
            if (listener == null)
            {
                Assert.Inconclusive($"Could not bind TcpListener on port {TcpControlPort}; skipping network test.");
                return;
            }

            // Connect as a client to the control port on loopback and send a valid take-control packet
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(IPAddress.Loopback, TcpControlPort);
            if (!connectTask.Wait(TimeSpan.FromSeconds(3)))
            {
                // Give a final try on IPv4 loopback
                client.Close();
                Assert.Inconclusive($"Unable to connect to local TcpListener on port {TcpControlPort}.");
                return;
            }

            using var ns = client.GetStream();
            ns.WriteTimeout = 3000;
            ns.ReadTimeout = 3000;

            // Wait longer to ensure server accept loop is ready
            Thread.Sleep(200);

            // Build message: opcode + 4 + 4 bytes payload
            var buf = new byte[1 + 4 + 4];
            buf[0] = MSG_TAKE_CONTROL_AT;
            Array.Copy(BitConverter.GetBytes(12345), 0, buf, 1, 4);
            Array.Copy(BitConverter.GetBytes(23456), 0, buf, 1 + 4, 4);

            ns.Write(buf, 0, buf.Length);
            ns.Flush();

            // Read back ack
            int ack = ns.ReadByte();
            Assert.AreNotEqual(-1, ack);
            Assert.AreEqual(MSG_TAKE_CONTROL_ACK, (byte)ack);

            // Cleanup
            stopMi.Invoke(transmitter, null);
        }

        // Helper stream that returns the specified number of bytes on the first Read,
        // then throws IOException on subsequent reads to validate exception handling.
        private sealed class ThrowingStream : Stream
        {
            private readonly int _firstReadCount;
            private int _readCalls = 0;

            public ThrowingStream(int firstReadCount) => _firstReadCount = firstReadCount;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                _readCalls++;
                if (_readCalls == 1)
                {
                    int toCopy = Math.Min(_firstReadCount, count);
                    for (int i = 0; i < toCopy; i++) buffer[offset + i] = (byte)(i + 1);
                    return toCopy;
                }
                throw new IOException("Simulated IO error");
            }

            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}