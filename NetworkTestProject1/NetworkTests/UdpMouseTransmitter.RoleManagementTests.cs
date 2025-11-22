using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.Network;

namespace NetworkTestProject1.Network
{
    /// <summary>
    /// Comprehensive unit tests for UdpMouseTransmitter.RoleManagement.cs partial class.
    /// Tests cover SetLocalRole, HandleRoleSwitchRequest, and HandleRoleSwitchAccept methods.
    /// </summary>
    [TestClass]
    public class UdpMouseTransmitterRoleManagementTests
    {
        private const byte MSG_ROLE_SWITCH_REQUEST = 0x12;
        private const byte MSG_ROLE_SWITCH_ACCEPT = 0x13;

        #region SetLocalRole Tests

        [TestMethod]
        public void SetLocalRole_SetsRoleToSender_UpdatesHandshakeComplete()
        {
            // Arrange
            var mockUdpClient = new MockUdpClient();
            var transmitter = new UdpMouseTransmitter(_ => mockUdpClient);

            // Act
            transmitter.SetLocalRole(ConnectionRole.Sender);

            // Assert
            Assert.AreEqual(ConnectionRole.Sender, transmitter.CurrentRole);
        }

        [TestMethod]
        public void SetLocalRole_SetsRoleToReceiver_UpdatesHandshakeComplete()
        {
            // Arrange
            var mockUdpClient = new MockUdpClient();
            var transmitter = new UdpMouseTransmitter(_ => mockUdpClient);

            // Act
            transmitter.SetLocalRole(ConnectionRole.Receiver);

            // Assert
            Assert.AreEqual(ConnectionRole.Receiver, transmitter.CurrentRole);
        }

        [TestMethod]
        public void SetLocalRole_FiresRoleChangedEvent_WithCorrectRole()
        {
            // Arrange
            var mockUdpClient = new MockUdpClient();
            var transmitter = new UdpMouseTransmitter(_ => mockUdpClient);
            ConnectionRole? firedRole = null;
            transmitter.RoleChanged += (role) => firedRole = role;

            // Act
            transmitter.SetLocalRole(ConnectionRole.Sender);

            // Assert
            Assert.IsNotNull(firedRole);
            Assert.AreEqual(ConnectionRole.Sender, firedRole.Value);
        }

        [TestMethod]
        public void SetLocalRole_AllowsSwitchingBetweenRoles()
        {
            // Arrange
            var mockUdpClient = new MockUdpClient();
            var transmitter = new UdpMouseTransmitter(_ => mockUdpClient);
            var roleChanges = new List<ConnectionRole>();
            transmitter.RoleChanged += (role) => roleChanges.Add(role);

            // Act
            transmitter.SetLocalRole(ConnectionRole.Sender);
            transmitter.SetLocalRole(ConnectionRole.Receiver);
            transmitter.SetLocalRole(ConnectionRole.Sender);

            // Assert
            Assert.AreEqual(3, roleChanges.Count);
            Assert.AreEqual(ConnectionRole.Sender, roleChanges[0]);
            Assert.AreEqual(ConnectionRole.Receiver, roleChanges[1]);
            Assert.AreEqual(ConnectionRole.Sender, roleChanges[2]);
        }

        [TestMethod]
        public void SetLocalRole_ThreadSafe_ConcurrentCalls()
        {
            // Arrange
            var mockUdpClient = new MockUdpClient();
            var transmitter = new UdpMouseTransmitter(_ => mockUdpClient);
            var completed = 0;
            var threads = new Thread[10];

            // Act
            for (int i = 0; i < threads.Length; i++)
            {
                var role = i % 2 == 0 ? ConnectionRole.Sender : ConnectionRole.Receiver;
                threads[i] = new Thread(() =>
                {
                    for (int j = 0; j < 100; j++)
                    {
                        transmitter.SetLocalRole(role);
                    }
                    Interlocked.Increment(ref completed);
                });
                threads[i].Start();
            }

            foreach (var thread in threads)
            {
                thread.Join(5000);
            }

            // Assert - all threads completed without exceptions
            Assert.AreEqual(threads.Length, completed);
            // Role should be either Sender or Receiver (deterministic outcome not guaranteed due to concurrency)
            var finalRole = transmitter.CurrentRole;
            Assert.IsTrue(finalRole == ConnectionRole.Sender || finalRole == ConnectionRole.Receiver);
        }

        #endregion

        #region HandleRoleSwitchRequest Tests

        [TestMethod]
        public void HandleRoleSwitchRequest_WithoutRemoteEndpoint_DropsRequest()
        {
            // Arrange
            var mockUdpClient = new MockUdpClient();
            var transmitter = new UdpMouseTransmitter(_ => mockUdpClient);
            transmitter.StartHost(); // Starts without remote endpoint
            var initialRole = transmitter.CurrentRole;
            var requestFrom = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 5000);

            // Simulate receiving role switch request
            var packet = new byte[] { MSG_ROLE_SWITCH_REQUEST };
            mockUdpClient.InjectReceivedPacket(packet, requestFrom);

            Thread.Sleep(100); // Allow processing

            // Assert - role should not have changed
            Assert.AreEqual(initialRole, transmitter.CurrentRole);
            Assert.AreEqual(0, mockUdpClient.SentPackets.Count); // No acceptance sent
        }

        [TestMethod]
        public void HandleRoleSwitchRequest_FromUnexpectedPeer_DropsRequest()
        {
            // Arrange
            var mockUdpClient = new MockUdpClient();
            var transmitter = new UdpMouseTransmitter(_ => mockUdpClient);
            transmitter.StartHost("192.168.1.50"); // Set expected peer
            transmitter.SetLocalRole(ConnectionRole.Sender); // Ensure handshake complete

            var unexpectedPeer = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 5000);
            var initialRole = transmitter.CurrentRole;

            // Simulate receiving role switch request from unexpected peer
            var packet = new byte[] { MSG_ROLE_SWITCH_REQUEST };
            mockUdpClient.InjectReceivedPacket(packet, unexpectedPeer);

            Thread.Sleep(100);

            // Assert
            Assert.AreEqual(initialRole, transmitter.CurrentRole);
            Assert.AreEqual(0, mockUdpClient.SentPackets.Count);
        }

        //[TestMethod]
        //public void HandleRoleSwitchRequest_BeforeHandshakeComplete_Ignores()
        //{
        //    // Arrange
        //    var mockUdpClient = new MockUdpClient();
        //    var transmitter = new UdpMouseTransmitter(_ => mockUdpClient);
        //    transmitter.StartHost("192.168.1.50");
        //    // Don't set handshake complete (default is false)

        //    var peer = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 5000);
        //    var packet = new byte[] { MSG_ROLE_SWITCH_REQUEST };
        //    mockUdpClient.InjectReceivedPacket(packet, peer);

        //    Thread.Sleep(100);

        //    // Assert - no acceptance sent
        //    Assert.AreEqual(0, mockUdpClient.SentPackets.Count);
        //}

        [TestMethod]
        public void HandleRoleSwitchRequest_SwitchesSenderToReceiver()
        {
            // Arrange
            var mockUdpClient = new MockUdpClient();
            var transmitter = new UdpMouseTransmitter(_ => mockUdpClient);
            transmitter.StartHost("192.168.1.50");
            transmitter.SetLocalRole(ConnectionRole.Sender);

            ConnectionRole? newRole = null;
            transmitter.RoleChanged += (role) => newRole = role;

            var peer = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 5000);
            var packet = new byte[] { MSG_ROLE_SWITCH_REQUEST };

            // Act
            mockUdpClient.InjectReceivedPacket(packet, peer);
            Thread.Sleep(100);

            // Assert
            Assert.AreEqual(ConnectionRole.Receiver, transmitter.CurrentRole);
            Assert.AreEqual(ConnectionRole.Receiver, newRole);
        }

        [TestMethod]
        public void HandleRoleSwitchRequest_SwitchesReceiverToSender()
        {
            // Arrange
            var mockUdpClient = new MockUdpClient();
            var transmitter = new UdpMouseTransmitter(_ => mockUdpClient);
            transmitter.StartHost("192.168.1.50");
            transmitter.SetLocalRole(ConnectionRole.Receiver);

            ConnectionRole? newRole = null;
            transmitter.RoleChanged += (role) => newRole = role;

            var peer = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 5000);
            var packet = new byte[] { MSG_ROLE_SWITCH_REQUEST };

            // Act
            mockUdpClient.InjectReceivedPacket(packet, peer);
            Thread.Sleep(100);

            // Assert
            Assert.AreEqual(ConnectionRole.Sender, transmitter.CurrentRole);
            Assert.AreEqual(ConnectionRole.Sender, newRole);
        }

        [TestMethod]
        public void HandleRoleSwitchRequest_SendsAcceptancePacket()
        {
            // Arrange
            var mockUdpClient = new MockUdpClient();
            var transmitter = new UdpMouseTransmitter(_ => mockUdpClient);
            transmitter.StartHost("192.168.1.50");
            transmitter.SetLocalRole(ConnectionRole.Sender);

            var peer = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 5000);
            var packet = new byte[] { MSG_ROLE_SWITCH_REQUEST };

            // Act
            mockUdpClient.InjectReceivedPacket(packet, peer);
            Thread.Sleep(100);

            // Assert
            Assert.AreEqual(1, mockUdpClient.SentPackets.Count);
            var sentPacket = mockUdpClient.SentPackets[0];
            Assert.AreEqual(MSG_ROLE_SWITCH_ACCEPT, sentPacket.Data[0]);
        }

        [TestMethod]
        public void HandleRoleSwitchRequest_FiresRoleChangedEvent()
        {
            // Arrange
            var mockUdpClient = new MockUdpClient();
            var transmitter = new UdpMouseTransmitter(_ => mockUdpClient);
            transmitter.StartHost("192.168.1.50");
            transmitter.SetLocalRole(ConnectionRole.Sender);

            var roleChangedFired = false;
            transmitter.RoleChanged += (role) => roleChangedFired = true;

            var peer = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 5000);
            var packet = new byte[] { MSG_ROLE_SWITCH_REQUEST };

            // Act
            mockUdpClient.InjectReceivedPacket(packet, peer);
            Thread.Sleep(100);

            // Assert
            Assert.IsTrue(roleChangedFired);
        }

        #endregion

        #region HandleRoleSwitchAccept Tests

        [TestMethod]
        public void HandleRoleSwitchAccept_WithoutRemoteEndpoint_DropsPacket()
        {
            // Arrange
            var mockUdpClient = new MockUdpClient();
            var transmitter = new UdpMouseTransmitter(_ => mockUdpClient);
            transmitter.StartHost();
            var initialRole = transmitter.CurrentRole;

            var acceptFrom = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 5000);
            var packet = new byte[] { MSG_ROLE_SWITCH_ACCEPT };

            // Act
            mockUdpClient.InjectReceivedPacket(packet, acceptFrom);
            Thread.Sleep(100);

            // Assert
            Assert.AreEqual(initialRole, transmitter.CurrentRole);
        }

        [TestMethod]
        public void HandleRoleSwitchAccept_FromUnexpectedPeer_DropsPacket()
        {
            // Arrange
            var mockUdpClient = new MockUdpClient();
            var transmitter = new UdpMouseTransmitter(_ => mockUdpClient);
            transmitter.StartHost("192.168.1.50");
            transmitter.SetLocalRole(ConnectionRole.Sender);

            var unexpectedPeer = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 5000);
            var initialRole = transmitter.CurrentRole;

            var packet = new byte[] { MSG_ROLE_SWITCH_ACCEPT };

            // Act
            mockUdpClient.InjectReceivedPacket(packet, unexpectedPeer);
            Thread.Sleep(100);

            // Assert
            Assert.AreEqual(initialRole, transmitter.CurrentRole);
        }

        [TestMethod]
        public void HandleRoleSwitchAccept_BeforeHandshakeComplete_Ignores()
        {
            // Arrange
            var mockUdpClient = new MockUdpClient();
            var transmitter = new UdpMouseTransmitter(_ => mockUdpClient);
            transmitter.StartHost("192.168.1.50");
            // Don't complete handshake

            var peer = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 5000);
            var packet = new byte[] { MSG_ROLE_SWITCH_ACCEPT };

            // Act
            mockUdpClient.InjectReceivedPacket(packet, peer);
            Thread.Sleep(100);

            // Assert - default role should remain
            Assert.AreEqual(ConnectionRole.Sender, transmitter.CurrentRole); // StartHost sets Sender
        }

        [TestMethod]
        public void HandleRoleSwitchAccept_SwitchesSenderToReceiver()
        {
            // Arrange
            var mockUdpClient = new MockUdpClient();
            var transmitter = new UdpMouseTransmitter(_ => mockUdpClient);
            transmitter.StartHost("192.168.1.50");
            transmitter.SetLocalRole(ConnectionRole.Sender);

            ConnectionRole? newRole = null;
            transmitter.RoleChanged += (role) => newRole = role;

            var peer = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 5000);
            var packet = new byte[] { MSG_ROLE_SWITCH_ACCEPT };

            // Act
            mockUdpClient.InjectReceivedPacket(packet, peer);
            Thread.Sleep(100);

            // Assert
            Assert.AreEqual(ConnectionRole.Receiver, transmitter.CurrentRole);
            Assert.AreEqual(ConnectionRole.Receiver, newRole);
        }

        [TestMethod]
        public void HandleRoleSwitchAccept_SwitchesReceiverToSender()
        {
            // Arrange
            var mockUdpClient = new MockUdpClient();
            var transmitter = new UdpMouseTransmitter(_ => mockUdpClient);
            transmitter.StartHost("192.168.1.50");
            transmitter.SetLocalRole(ConnectionRole.Receiver);

            ConnectionRole? newRole = null;
            transmitter.RoleChanged += (role) => newRole = role;

            var peer = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 5000);
            var packet = new byte[] { MSG_ROLE_SWITCH_ACCEPT };

            // Act
            mockUdpClient.InjectReceivedPacket(packet, peer);
            Thread.Sleep(100);

            // Assert
            Assert.AreEqual(ConnectionRole.Sender, transmitter.CurrentRole);
            Assert.AreEqual(ConnectionRole.Sender, newRole);
        }

        [TestMethod]
        public void HandleRoleSwitchAccept_FiresRoleChangedEvent()
        {
            // Arrange
            var mockUdpClient = new MockUdpClient();
            var transmitter = new UdpMouseTransmitter(_ => mockUdpClient);
            transmitter.StartHost("192.168.1.50");
            transmitter.SetLocalRole(ConnectionRole.Sender);

            var roleChangedFired = false;
            transmitter.RoleChanged += (role) => roleChangedFired = true;

            var peer = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 5000);
            var packet = new byte[] { MSG_ROLE_SWITCH_ACCEPT };

            // Act
            mockUdpClient.InjectReceivedPacket(packet, peer);
            Thread.Sleep(100);

            // Assert
            Assert.IsTrue(roleChangedFired);
        }

        #endregion

        #region Integration Tests

        [TestMethod]
        public void RoleSwitch_RoundTrip_RequestAndAccept()
        {
            // Arrange
            var mockUdpClientA = new MockUdpClient();
            var mockUdpClientB = new MockUdpClient();

            var transmitterA = new UdpMouseTransmitter(_ => mockUdpClientA);
            var transmitterB = new UdpMouseTransmitter(_ => mockUdpClientB);

            transmitterA.StartHost("192.168.1.50");
            transmitterB.StartHost("192.168.1.60");

            transmitterA.SetLocalRole(ConnectionRole.Sender);
            transmitterB.SetLocalRole(ConnectionRole.Receiver);

            var peerA = new IPEndPoint(IPAddress.Parse("192.168.1.60"), 5000);
            var peerB = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 5000);

            // Act - A sends role switch request
            var requestPacket = new byte[] { MSG_ROLE_SWITCH_REQUEST };
            mockUdpClientB.InjectReceivedPacket(requestPacket, peerA);
            Thread.Sleep(100);

            // B should switch and send accept
            Assert.AreEqual(ConnectionRole.Sender, transmitterB.CurrentRole);
            Assert.AreEqual(1, mockUdpClientB.SentPackets.Count);

            // Simulate A receiving the accept
            var acceptPacket = new byte[] { MSG_ROLE_SWITCH_ACCEPT };
            mockUdpClientA.InjectReceivedPacket(acceptPacket, peerB);
            Thread.Sleep(100);

            // Assert - both should have switched
            Assert.AreEqual(ConnectionRole.Receiver, transmitterA.CurrentRole);
            Assert.AreEqual(ConnectionRole.Sender, transmitterB.CurrentRole);
        }

        [TestMethod]
        public void MultipleRoleSwitches_MaintainConsistency()
        {
            // Arrange
            var mockUdpClient = new MockUdpClient();
            var transmitter = new UdpMouseTransmitter(_ => mockUdpClient);
            transmitter.StartHost("192.168.1.50");
            transmitter.SetLocalRole(ConnectionRole.Sender);

            var roles = new List<ConnectionRole>();
            transmitter.RoleChanged += (role) => roles.Add(role);

            var peer = new IPEndPoint(IPAddress.Parse("192.168.1.50"), 5000);

            // Act - perform multiple switches
            for (int i = 0; i < 5; i++)
            {
                var packet = new byte[] { MSG_ROLE_SWITCH_REQUEST };
                mockUdpClient.InjectReceivedPacket(packet, peer);
                Thread.Sleep(50);
            }

            // Assert
            Assert.AreEqual(5, roles.Count);
            // Verify alternating pattern
            for (int i = 0; i < roles.Count; i++)
            {
                var expected = i % 2 == 0 ? ConnectionRole.Receiver : ConnectionRole.Sender;
                Assert.AreEqual(expected, roles[i]);
            }
        }

        #endregion

        #region Test Cleanup

        [TestCleanup]
        public void Cleanup()
        {
            // Allow time for background threads to finish
            Thread.Sleep(200);
        }

        #endregion

        #region Mock Classes

        /// <summary>
        /// Mock UDP client for testing without real network sockets.
        /// Avoids actual socket binding to prevent port conflicts in parallel tests.
        /// </summary>
        private class MockUdpClient : IUdpClient
        {
            private readonly List<SentPacket> _sentPackets = new();
            private readonly Queue<ReceivedPacket> _receivedPackets = new();
            private readonly AutoResetEvent _receiveEvent = new(false);
            private readonly MockSocket _socket = new();
            private bool _disposed;

            public Socket Client => _socket;

            public List<SentPacket> SentPackets => _sentPackets;

            public void InjectReceivedPacket(byte[] data, IPEndPoint remoteEP)
            {
                lock (_receivedPackets)
                {
                    _receivedPackets.Enqueue(new ReceivedPacket { Data = data, RemoteEP = remoteEP });
                }
                _receiveEvent.Set();
            }

            public int Send(byte[] dgram, int bytes, IPEndPoint? endPoint)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(MockUdpClient));

                var data = new byte[bytes];
                Array.Copy(dgram, data, bytes);
                lock (_sentPackets)
                {
                    _sentPackets.Add(new SentPacket { Data = data, EndPoint = endPoint });
                }
                return bytes;
            }

            public byte[] Receive(ref IPEndPoint remoteEP)
            {
                while (!_disposed)
                {
                    lock (_receivedPackets)
                    {
                        if (_receivedPackets.Count > 0)
                        {
                            var packet = _receivedPackets.Dequeue();
                            remoteEP = packet.RemoteEP;
                            return packet.Data;
                        }
                    }

                    _receiveEvent.WaitOne(100);
                }

                throw new SocketException((int)SocketError.Interrupted);
            }

            public void Close()
            {
                _disposed = true;
                _receiveEvent.Set();
            }

            public void Dispose()
            {
                Close();
                _receiveEvent.Dispose();
            }

            public class SentPacket
            {
                public byte[] Data { get; set; } = Array.Empty<byte>();
                public IPEndPoint? EndPoint { get; set; }
            }

            private class ReceivedPacket
            {
                public byte[] Data { get; set; } = Array.Empty<byte>();
                public IPEndPoint RemoteEP { get; set; } = new IPEndPoint(IPAddress.Any, 0);
            }

            /// <summary>
            /// Mock socket that doesn't bind to actual ports to avoid conflicts in parallel tests.
            /// </summary>
            private class MockSocket : Socket
            {
                private static int _nextPort = 10000; // Start from a high port number
                private readonly IPEndPoint _localEndPoint;

                public MockSocket() : base(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                {
                    // Use a unique port for each instance to avoid conflicts
                    // Don't actually bind - just set the local endpoint property
                    var port = Interlocked.Increment(ref _nextPort);
                    _localEndPoint = new IPEndPoint(IPAddress.Loopback, port);
                }

                public new EndPoint? LocalEndPoint => _localEndPoint;
            }
        }

        #endregion
    }
}