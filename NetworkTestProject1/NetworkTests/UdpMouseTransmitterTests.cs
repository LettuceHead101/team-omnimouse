using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.Network;

namespace NetworkTestProject1.Network
{
	[TestClass]
	public class UdpMouseTransmitterBasicTests
	{
		// minimal mock IUdpClient to avoid real network usage
		private class MockUdpClient : IUdpClient
		{
			private class MockSocket : Socket
			{
				private static int _nextPort = 20000;
				private readonly EndPoint _localEndPoint;
				public MockSocket() : base(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
				{
					var port = Interlocked.Increment(ref _nextPort);
					_localEndPoint = new IPEndPoint(IPAddress.Loopback, port);
				}
				public new EndPoint LocalEndPoint => _localEndPoint;
			}

			private bool _disposed;
			private readonly MockSocket _socket = new();
			public Socket Client => _socket;
			public void Close() { _disposed = true; }
			public int Send(byte[] dgram, int bytes, IPEndPoint? endPoint) => bytes;
			public byte[] Receive(ref IPEndPoint remoteEP)
			{
				// Block briefly then simulate interruption so receive loop can progress
				Thread.Sleep(10);
				throw new SocketException((int)SocketError.Interrupted);
			}
			public void Dispose() { _disposed = true; }
		}

		private static T GetPrivateField<T>(object instance, string name)
		{
			var f = instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
			Assert.IsNotNull(f, $"Field '{name}' not found");
			var val = f!.GetValue(instance);
			return (T)val!;
		}

		[TestMethod]
		public void RegisterClientEndpoint_AddsMapping()
		{
			var transmitter = new UdpMouseTransmitter(_ => new MockUdpClient());
			var ep = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6000);
			transmitter.RegisterClientEndpoint("clientA", ep);

			var dict = GetPrivateField<Dictionary<string, IPEndPoint>>(transmitter, "_clientEndpoints");
			Assert.IsTrue(dict.TryGetValue("clientA", out var stored));
			Assert.AreEqual(ep, stored);
		}

		[TestMethod]
		public void UnregisterClientEndpoint_RemovesMapping()
		{
			var transmitter = new UdpMouseTransmitter(_ => new MockUdpClient());
			var ep = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 6001);
			transmitter.RegisterClientEndpoint("clientB", ep);
			transmitter.UnregisterClientEndpoint("clientB");

			var dict = GetPrivateField<Dictionary<string, IPEndPoint>>(transmitter, "_clientEndpoints");
			Assert.IsFalse(dict.ContainsKey("clientB"));
		}

		[TestMethod]
		public void StartHost_SetsSenderRoleAndNotHandshakeComplete()
		{
			var transmitter = new UdpMouseTransmitter(_ => new MockUdpClient());
			transmitter.StartHost();
			Assert.AreEqual(ConnectionRole.Sender, transmitter.CurrentRole);
			var hsComplete = GetPrivateField<bool>(transmitter, "_handshakeComplete");
			Assert.IsFalse(hsComplete);
		}

		[TestMethod]
		public void StartCoHost_SetsReceiverRoleAndRemoteEndpoint()
		{
			var transmitter = new UdpMouseTransmitter(_ => new MockUdpClient());
			transmitter.StartCoHost("192.0.2.50"); // TEST-NET-1 IP
			Assert.AreEqual(ConnectionRole.Receiver, transmitter.CurrentRole);
			var remote = GetPrivateField<IPEndPoint?>(transmitter, "_remoteEndPoint");
			Assert.IsNotNull(remote);
			Assert.AreEqual("192.0.2.50", remote!.Address.ToString());
			Assert.AreEqual(5000, remote.Port); // UdpPort constant
		}

		[TestMethod]
		public void SetRemotePeer_AfterStartHost_SetsEndpointAndResetsHandshake()
		{
			var transmitter = new UdpMouseTransmitter(_ => new MockUdpClient());
			transmitter.StartHost();
			transmitter.SetRemotePeer("198.51.100.10"); // TEST-NET-2
			var remote = GetPrivateField<IPEndPoint?>(transmitter, "_remoteEndPoint");
			Assert.IsNotNull(remote);
			Assert.AreEqual("198.51.100.10", remote!.Address.ToString());
			var hsComplete = GetPrivateField<bool>(transmitter, "_handshakeComplete");
			Assert.IsFalse(hsComplete); // reset expected
		}

		[TestMethod]
		public void Disconnect_CleansResources()
		{
			var transmitter = new UdpMouseTransmitter(_ => new MockUdpClient());
			transmitter.StartHost();
			transmitter.RegisterClientEndpoint("clientX", new IPEndPoint(IPAddress.Loopback, 6002));
			transmitter.Disconnect();

			var udpClient = GetPrivateField<IUdpClient?>(transmitter, "_udpClient");
			Assert.IsNull(udpClient);
			var running = GetPrivateField<bool>(transmitter, "_running");
			Assert.IsFalse(running);
		}

		[TestMethod]
		public void LogDiagnostics_DoesNotThrow()
		{
			var transmitter = new UdpMouseTransmitter(_ => new MockUdpClient());
			transmitter.StartHost();
			transmitter.SetRemotePeer("203.0.113.25"); // TEST-NET-3
			transmitter.LogDiagnostics(); // Should not throw even early in lifecycle
		}
	}
}
