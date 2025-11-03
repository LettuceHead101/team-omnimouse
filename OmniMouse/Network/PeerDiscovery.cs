using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OmniMouse.Model;
using OmniMouse.Storage;

namespace OmniMouse.Network
{
    // Simple UDP broadcast discovery: broadcasts "hello" and listens for peers.
    // Port 57777 for discovery, port 5000 remains data channel.
    public sealed class PeerDiscovery : IDisposable
    {
        private const int DiscoveryPort = 57777;
        private const string Probe = "omnimouse/probe";
        private const string Hello = "omnimouse/hello";
        private readonly UdpClient _udpRx;
        private readonly UdpClient _udpTx;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<Guid, PeerInfo> _peers = new();
        private Task? _rxTask;
        private Task? _txTask;

        public event Action<PeerInfo>? PeerUp;
        public event Action<PeerInfo>? PeerDown;

        public PeerDiscovery()
        {
            _udpRx = new UdpClient(new IPEndPoint(IPAddress.Any, DiscoveryPort)) { EnableBroadcast = true };
            _udpTx = new UdpClient() { EnableBroadcast = true };
        }

        public void Start()
        {
            if (_rxTask != null) return;
            _rxTask = Task.Run(ReceiveLoop);
            _txTask = Task.Run(SendLoop);
            // Kick a probe so others reply quickly
            _ = Task.Run(() => SendFrame(Probe));
        }

        public PeerInfo[] Snapshot() => _peers.Values.ToArray();

        public async Task<PeerInfo?> WaitForFirstPeerAsync(TimeSpan timeout)
        {
            var until = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < until)
            {
                var arr = Snapshot();
                foreach (var p in arr)
                {
                    if (p.Id != DeviceIdentity.Id) return p;
                }
                await Task.Delay(100);
            }
            return null;
        }

        private async Task SendLoop()
        {
            var me = new
            {
                t = Hello,
                id = DeviceIdentity.Id,
                name = DeviceIdentity.Name,
                port = 5000
            };

            while (!_cts.IsCancellationRequested)
            {
                try { await SendJson(me); } catch { }
                await Task.Delay(2000, _cts.Token).ContinueWith(_ => { });
            }
        }

        private async Task ReceiveLoop()
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpRx.ReceiveAsync(_cts.Token);
                    var text = Encoding.UTF8.GetString(result.Buffer);
                    if (text.StartsWith("{"))
                    {
                        using var doc = JsonDocument.Parse(text);
                        if (!doc.RootElement.TryGetProperty("t", out var tProp)) continue;
                        var t = tProp.GetString();
                        if (t == Hello)
                        {
                            var id = doc.RootElement.GetProperty("id").GetGuid();
                            if (id == DeviceIdentity.Id) continue; // ignore self
                            var name = doc.RootElement.GetProperty("name").GetString() ?? "Unknown";
                            var port = doc.RootElement.GetProperty("port").GetInt32();
                            var ip = result.RemoteEndPoint.Address.ToString();

                            var info = new PeerInfo
                            {
                                Id = id,
                                Name = name,
                                Ip = ip,
                                Port = port,
                                LastSeenUtc = DateTime.UtcNow
                            };

                            var added = _peers.AddOrUpdate(id, info, (_, old) =>
                            {
                                old.Name = info.Name;
                                old.Ip = info.Ip;
                                old.Port = info.Port;
                                old.LastSeenUtc = info.LastSeenUtc;
                                return old;
                            });

                            KnownPeersStore.Upsert(added);
                            PeerUp?.Invoke(added);
                        }
                        else if (t == Probe)
                        {
                            // someone is asking; respond with hello
                            var me = new { t = Hello, id = DeviceIdentity.Id, name = DeviceIdentity.Name, port = 5000 };
                            await SendJson(me);
                        }
                    }

                    // prune stale peers occasionally
                    foreach (var kv in _peers)
                    {
                        if ((DateTime.UtcNow - kv.Value.LastSeenUtc).TotalSeconds > 6)
                        {
                            if (_peers.TryRemove(kv.Key, out var removed))
                                PeerDown?.Invoke(removed);
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { await Task.Delay(50); }
            }
        }

        private Task SendJson(object obj)
        {
            var json = JsonSerializer.Serialize(obj);
            var bytes = Encoding.UTF8.GetBytes(json);
            return SendFrame(bytes);
        }

        private Task SendFrame(string text) => SendFrame(Encoding.UTF8.GetBytes(text));
        private async Task SendFrame(byte[] bytes)
        {
            var ep = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
            await _udpTx.SendAsync(bytes, bytes.Length, ep);
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _udpRx.Close(); } catch { }
            try { _udpTx.Close(); } catch { }
            _udpRx.Dispose();
            _udpTx.Dispose();
            _cts.Dispose();
        }
    }
}