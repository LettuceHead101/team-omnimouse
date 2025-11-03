using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OmniMouse.Model;

namespace OmniMouse.Storage
{
    public static class KnownPeersStore
    {
        private sealed class StoreModel { public List<PeerInfo> Peers { get; set; } = new(); }
        private static readonly string s_dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmniMouse");
        private static readonly string s_file = Path.Combine(s_dir, "known-peers.json");

        public static IReadOnlyList<PeerInfo> Load()
        {
            try
            {
                if (File.Exists(s_file))
                {
                    var json = File.ReadAllText(s_file);
                    var m = JsonSerializer.Deserialize<StoreModel>(json);
                    return m?.Peers ?? new List<PeerInfo>();
                }
            }
            catch { }
            return new List<PeerInfo>();
        }

        public static void Upsert(PeerInfo peer)
        {
            try
            {
                Directory.CreateDirectory(s_dir);
                var peers = Load().ToList();
                var idx = peers.FindIndex(p => p.Id == peer.Id || (!string.IsNullOrWhiteSpace(peer.Ip) && p.Ip == peer.Ip));
                if (idx >= 0) peers[idx] = peer; else peers.Add(peer);
                var json = JsonSerializer.Serialize(new StoreModel { Peers = peers }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(s_file, json);
            }
            catch { }
        }
    }
}