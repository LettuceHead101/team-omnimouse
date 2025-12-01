using System;
using System.Text;

namespace OmniMouse.Network
{
    public partial class UdpMouseTransmitter
    {
        /// <summary>
        /// Sends local monitor information to the peer after handshake completes.
        /// Format: [MSG_MONITOR_INFO][monitorCount:int32]
        ///         For each monitor: [friendlyNameLen:int32][friendlyName:UTF8]
        ///                           [localBounds:4xint32][globalBounds:4xint32]
        ///                           [isPrimary:byte][ownerClientIdLen:int32][ownerClientId:UTF8]
        /// </summary>
        private void SendMonitorInfo()
        {
            if (_localScreenMap == null || string.IsNullOrEmpty(_localClientId))
            {
                Console.WriteLine("[UDP][MonitorSync] No local screen map registered; skipping monitor send");
                return;
            }

            if (_udpClient == null || _remoteEndPoint == null)
            {
                Console.WriteLine("[UDP][MonitorSync] No connection; cannot send monitors");
                return;
            }


            // Only send monitors owned by this client
            var allMonitors = _localScreenMap.GetMonitorsSnapshot();
            var monitors = allMonitors.Where(m => m.OwnerClientId == _localClientId).ToList();
            if (monitors.Count == 0)
            {
                Console.WriteLine("[UDP][MonitorSync] No local monitors to send");
                return;
            }

            try
            {
                // Build packet
                using var ms = new System.IO.MemoryStream();
                using var writer = new System.IO.BinaryWriter(ms);

                writer.Write(MSG_MONITOR_INFO);
                writer.Write(monitors.Count);

                foreach (var monitor in monitors)
                {
                    // Friendly name
                    var nameBytes = Encoding.UTF8.GetBytes(monitor.FriendlyName);
                    writer.Write(nameBytes.Length);
                    writer.Write(nameBytes);

                    // Local bounds (Left, Top, Width, Height)
                    writer.Write(monitor.LocalBounds.Left);
                    writer.Write(monitor.LocalBounds.Top);
                    writer.Write(monitor.LocalBounds.Width);
                    writer.Write(monitor.LocalBounds.Height);

                    // Global bounds (Left, Top, Width, Height)
                    writer.Write(monitor.GlobalBounds.Left);
                    writer.Write(monitor.GlobalBounds.Top);
                    writer.Write(monitor.GlobalBounds.Width);
                    writer.Write(monitor.GlobalBounds.Height);

                    // IsPrimary flag
                    writer.Write((byte)(monitor.IsPrimary ? 1 : 0));

                    // Owner client ID (send as peer's endpoint ID since peer needs to identify these monitors)
                    var ownerBytes = Encoding.UTF8.GetBytes(_localClientId);
                    writer.Write(ownerBytes.Length);
                    writer.Write(ownerBytes);

                    Console.WriteLine($"[UDP][MonitorSync] Prepared monitor: {monitor.FriendlyName}, Local={monitor.LocalBounds}, Global={monitor.GlobalBounds}, Owner={_localClientId}");
                }

                var packet = ms.ToArray();
                _udpClient.Send(packet, packet.Length, _remoteEndPoint);
                Console.WriteLine($"[UDP][MonitorSync] Sent {monitors.Count} monitor(s) to {_remoteEndPoint.Address}:{_remoteEndPoint.Port}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UDP][MonitorSync] Failed to send monitors: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles incoming MSG_MONITOR_INFO messages and adds peer monitors to the local VirtualScreenMap.
        /// </summary>
        private void HandleMonitorInfo(byte[] data, int offset)
        {
            // Queue the packet if screen map isn't ready yet
            if (_localScreenMap == null)
            {
                lock (_monitorSyncLock)
                {
                    // Make a copy of the data to queue
                    var dataCopy = new byte[data.Length];
                    Array.Copy(data, dataCopy, data.Length);
                    _pendingMonitorPackets.Enqueue((dataCopy, offset, _remoteEndPoint!));
                    Console.WriteLine($"[UDP][MonitorSync] Queued monitor packet (screen map not ready yet, queue size: {_pendingMonitorPackets.Count})");
                }
                return;
            }

            if (_remoteEndPoint == null)
            {
                Console.WriteLine("[UDP][MonitorSync] No remote endpoint; cannot process peer monitors");
                return;
            }

            try
            {
                using var ms = new System.IO.MemoryStream(data, offset, data.Length - offset);
                using var reader = new System.IO.BinaryReader(ms);

                int monitorCount = reader.ReadInt32();
                Console.WriteLine($"[UDP][MonitorSync] Receiving {monitorCount} monitor(s) from peer {_remoteEndPoint.Address}");

                // Use the peer's endpoint as their client ID
                var peerClientId = $"{_remoteEndPoint.Address}:{_remoteEndPoint.Port}";

                // Compute a horizontal offset to place peer monitors so they don't overlap local ones in global space.
                // Strategy: shift peer monitors to start just to the right of the furthest existing monitor.
                int placementOffsetX = 0;
                int maxRight = 0;
                var existing = _localScreenMap.GetMonitorsSnapshot();
                foreach (var m in existing)
                {
                    if (m.GlobalBounds.Right > maxRight) maxRight = m.GlobalBounds.Right;
                }
                placementOffsetX = maxRight;

                for (int i = 0; i < monitorCount; i++)
                {
                    // Read friendly name
                    int nameLen = reader.ReadInt32();
                    var nameBytes = reader.ReadBytes(nameLen);
                    string friendlyName = Encoding.UTF8.GetString(nameBytes);

                    // Read local bounds
                    int localLeft = reader.ReadInt32();
                    int localTop = reader.ReadInt32();
                    int localWidth = reader.ReadInt32();
                    int localHeight = reader.ReadInt32();

                    // Read global bounds
                    int globalLeft = reader.ReadInt32();
                    int globalTop = reader.ReadInt32();
                    int globalWidth = reader.ReadInt32();
                    int globalHeight = reader.ReadInt32();

                    // Read isPrimary
                    bool isPrimary = reader.ReadByte() == 1;

                    // Read original owner client ID (from sender's perspective)
                    int ownerLen = reader.ReadInt32();
                    var ownerBytes = reader.ReadBytes(ownerLen);
                    string originalOwnerId = Encoding.UTF8.GetString(ownerBytes);

                    // Create monitor info - use peerClientId as owner since this is THEIR monitor
                    var monitor = new MonitorInfo
                    {
                        FriendlyName = $"{friendlyName} (Remote)",
                        OwnerClientId = peerClientId,  // CRITICAL: Use peer's endpoint ID
                        IsPrimary = isPrimary,
                        LocalBounds = new RectInt(localLeft, localTop, localWidth, localHeight),
                        // Shift peer monitors in global space to avoid overlap with local monitors
                        GlobalBounds = new RectInt(globalLeft + placementOffsetX, globalTop, globalWidth, globalHeight)
                    };

                    // Add to local screen map
                    _localScreenMap.AddOrUpdateMonitor(monitor);
                    
                    Console.WriteLine($"[UDP][MonitorSync] Added peer monitor: {monitor.FriendlyName}, Owner={peerClientId}, Local={monitor.LocalBounds}, Global={monitor.GlobalBounds}");
                }

                Console.WriteLine($"[UDP][MonitorSync] Successfully added {monitorCount} peer monitor(s) to screen map");

                // Emit consolidated layout summary for runtime verification
                this.DumpLayoutSummary("[UDP][MonitorSync] Layout after peer monitors added");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UDP][MonitorSync] Failed to process monitor info: {ex.Message}");
            }
        }

        /// <summary>
        /// Logs a concise, ordered summary of all monitors from both computers.
        /// Order: by Global.Left then Global.Top.
        /// </summary>
        private void DumpLayoutSummary(string prefix)
        {
            try
            {
                if (this._localScreenMap == null)
                {
                    Console.WriteLine($"{prefix}: screen map not available");
                    return;
                }

                var snapshot = this._localScreenMap.GetMonitorsSnapshot();
                if (snapshot.Count == 0)
                {
                    Console.WriteLine($"{prefix}: no monitors present");
                    return;
                }

                var ordered = snapshot
                    .OrderBy(m => m.GlobalBounds.Left)
                    .ThenBy(m => m.GlobalBounds.Top)
                    .ToList();

                var sb = new StringBuilder();
                sb.Append("added monitor(s) new layout is: ");
                for (int i = 0; i < ordered.Count; i++)
                {
                    var m = ordered[i];
                    sb.Append($"[{i}] {m.FriendlyName} Owner={m.OwnerClientId} Global=({m.GlobalBounds.Left},{m.GlobalBounds.Top},{m.GlobalBounds.Width}x{m.GlobalBounds.Height})");
                    if (i < ordered.Count - 1)
                    {
                        sb.Append("; ");
                    }
                }

                Console.WriteLine($"{prefix}: {sb}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UDP][MonitorSync] Failed to dump layout summary: {ex.Message}");
            }
        }
    }
}
