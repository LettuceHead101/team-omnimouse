using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OmniMouse.Network
{
    /// <summary>
    /// Integer axis-aligned rectangle used internally by the virtual map.
    /// Independent of WPF / System.Drawing so domain remains UI-free.
    /// </summary>
    public readonly struct RectInt
    {
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }

        public int Left => X;
        public int Top => Y;
        public int Right => X + Width;
        public int Bottom => Y + Height;

        public RectInt(int x, int y, int width, int height)
        {
            if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public bool Contains(int x, int y) =>
            x >= Left && x < Right && y >= Top && y < Bottom;

        public bool Intersects(RectInt other) =>
            other.Left < Right && other.Right > Left && other.Top < Bottom && other.Bottom > Top;

        public override string ToString() => $"[{X},{Y} {Width}x{Height}]";
    }

    /// <summary>
    /// Monitor metadata and mapping information.
    /// LocalBounds: local coordinate system (typically 0..w-1,0..h-1).
    /// GlobalBounds: position/size in the virtual coordinate space.
    /// </summary>
    public sealed class MonitorInfo
    {
        public string MonitorId { get; init; } = Guid.NewGuid().ToString();
        public string OwnerClientId { get; init; } = string.Empty;
        public string FriendlyName { get; init; } = string.Empty;

        // Local pixel bounds (origin local coordinate; usually X=0,Y=0)
        public RectInt LocalBounds { get; set; }

        // Global/virtual bounds - must be non-overlapping across monitors for deterministic lookup.
        public RectInt GlobalBounds { get; set; }

        // DPI or scale (1.0 = 100%). Optional; useful for translating physical vs logical pixels.
        public float DpiScale { get; set; } = 1.0f;

        // Helpful flag
        public bool IsPrimary { get; set; } = false;

        public override string ToString() =>
    $"Monitor[{FriendlyName}, Owner={OwnerClientId}, Global={GlobalBounds}, Local={LocalBounds}, Primary={IsPrimary}]";
    }

    /// <summary>
    /// A lightweight client descriptor that groups monitors for a PC.
    /// The Id should be stable (GUID, machine name, or endpoint identifier).
    /// </summary>
    public sealed class ClientPc
    {
        public string ClientId { get; init; } = Guid.NewGuid().ToString();
        public string FriendlyName { get; set; } = string.Empty;

        // Monitors owned by this client (monitor ids index)
        public List<string> MonitorIds { get; } = new();
    }

    /// <summary>
    /// Spatial index interface for extensibility. Default implementation is a simple list scan.
    /// Replace with a quadtree / R-tree if needed.
    /// </summary>
    public interface ISpatialIndex
    {
        void Rebuild(IEnumerable<MonitorInfo> monitors);
        IEnumerable<MonitorInfo> QueryPoint(int x, int y);
    }

    internal sealed class SimpleListSpatialIndex : ISpatialIndex
    {
        private List<MonitorInfo> _monitors = new();

        public void Rebuild(IEnumerable<MonitorInfo> monitors)
        {
            _monitors = monitors.ToList();
        }

        public IEnumerable<MonitorInfo> QueryPoint(int x, int y)
        {
            foreach (var m in _monitors)
            {
                if (m.GlobalBounds.Contains(x, y))
                    yield return m;
            }
        }
    }

    /// <summary>
    /// The VirtualScreenMap is a pure-domain component that holds the global layout,
    /// provides lookup, translation and change notifications. Thread-safe for concurrent readers.
    /// </summary>
    public sealed class VirtualScreenMap
    {
        // Clients by id
        private readonly Dictionary<string, ClientPc> _clients = new(StringComparer.Ordinal);
        // Monitors by id
        private readonly Dictionary<string, MonitorInfo> _monitors = new(StringComparer.Ordinal);
        // Spatial index for fast lookup (pluggable)
        private ISpatialIndex _spatialIndex = new SimpleListSpatialIndex();

        // Reader/writer lock to allow concurrent reads and exclusive writes
        private readonly ReaderWriterLockSlim _rw = new();

        // Event fired on structural changes (add/remove/update). Consumers subscribe to refresh their caches.
        public event Action? LayoutChanged;

        public VirtualScreenMap() { }

        // Allow replacing the index (e.g., quadtree) before any heavy operations.
        public void ReplaceSpatialIndex(ISpatialIndex index)
        {
            if (index is null) throw new ArgumentNullException(nameof(index));
            _rw.EnterWriteLock();
            try
            {
                _spatialIndex = index;
                _spatialIndex.Rebuild(_monitors.Values);
            }
            finally { _rw.ExitWriteLock(); }
        }

        // Client / monitor management
        public void AddOrUpdateClient(ClientPc client)
        {
            if (client is null) throw new ArgumentNullException(nameof(client));
            _rw.EnterWriteLock();
            try
            {
                _clients[client.ClientId] = client;
                OnLayoutChangedLocked();
            }
            finally { _rw.ExitWriteLock(); }
        }

        public bool RemoveClient(string clientId)
        {
            if (clientId is null) throw new ArgumentNullException(nameof(clientId));
            _rw.EnterWriteLock();
            try
            {
                if (!_clients.Remove(clientId)) return false;

                // remove monitors owned by that client
                var toRemove = _monitors.Values.Where(m => m.OwnerClientId == clientId).Select(m => m.MonitorId).ToList();
                foreach (var id in toRemove) _monitors.Remove(id);

                OnLayoutChangedLocked();
                return true;
            }
            finally { _rw.ExitWriteLock(); }
        }

        public void AddOrUpdateMonitor(MonitorInfo monitor)
        {
            if (monitor is null) throw new ArgumentNullException(nameof(monitor));
            _rw.EnterWriteLock();
            try
            {
                // Validate no overlapping monitors
                foreach (var existing in _monitors.Values)
                {
                    if (existing.MonitorId != monitor.MonitorId &&
                        existing.GlobalBounds.Intersects(monitor.GlobalBounds))
                    {
                        throw new InvalidOperationException(
                            $"Monitor '{monitor.FriendlyName}' overlaps with '{existing.FriendlyName}' in global space.");
                    }
                }

                _monitors[monitor.MonitorId] = monitor;

                // Ensure client references list is consistent
                if (!string.IsNullOrEmpty(monitor.OwnerClientId) && _clients.TryGetValue(monitor.OwnerClientId, out var client))
                {
                    if (!client.MonitorIds.Contains(monitor.MonitorId))
                        client.MonitorIds.Add(monitor.MonitorId);
                }

                OnLayoutChangedLocked();
            }
            finally { _rw.ExitWriteLock(); }
        }

        /// <summary>
        /// Checks if a monitor with the given global bounds can be placed without overlapping existing monitors.
        /// </summary>
        /// <param name="globalBounds">The proposed global bounds for the monitor.</param>
        /// <param name="excludeMonitorId">Optional monitor ID to exclude from overlap check (useful for updates).</param>
        /// <returns>True if the monitor can be placed, false if it would overlap.</returns>
        public bool CanPlaceMonitor(RectInt globalBounds, string? excludeMonitorId = null)
        {
            _rw.EnterReadLock();
            try
            {
                foreach (var existing in _monitors.Values)
                {
                    if (excludeMonitorId != null && existing.MonitorId == excludeMonitorId)
                        continue;

                    if (existing.GlobalBounds.Intersects(globalBounds))
                        return false;
                }
                return true;
            }
            finally { _rw.ExitReadLock(); }
        }

        public bool RemoveMonitor(string monitorId)
        {
            if (monitorId is null) throw new ArgumentNullException(nameof(monitorId));
            _rw.EnterWriteLock();
            try
            {
                if (!_monitors.Remove(monitorId)) return false;
                foreach (var c in _clients.Values)
                    c.MonitorIds.Remove(monitorId);

                OnLayoutChangedLocked();
                return true;
            }
            finally { _rw.ExitWriteLock(); }
        }

        // Recompute spatial index after bulk changes (or call after many single updates).
        public void RebuildIndex()
        {
            _rw.EnterWriteLock();
            try
            {
                _spatialIndex.Rebuild(_monitors.Values);
                OnLayoutChangedLocked(suppressEvent: true);
            }
            finally { _rw.ExitWriteLock(); }
            // Fire event outside lock
            LayoutChanged?.Invoke();
        }

        // Find monitor owning a global coordinate. If multiple monitors overlap, returns the first match.
        public MonitorInfo? FindMonitorAt(int globalX, int globalY)
        {
            _rw.EnterReadLock();
            try
            {
                return FindMonitorAtLocked(globalX, globalY);
            }
            finally { _rw.ExitReadLock(); }
        }

        // Internal helper that queries the spatial index without taking any locks.
        // Callers that hold locks may use this to avoid recursive lock acquisition.
        private MonitorInfo? FindMonitorAtLocked(int globalX, int globalY)
        {
            foreach (var m in _spatialIndex.QueryPoint(globalX, globalY))
                return m;
            return null;
        }

        // Translate a global point into local coordinates on the owning monitor.
        // Returns false if point lies outside any monitor.
        public bool TranslateGlobalToLocal(int globalX, int globalY, out MonitorInfo? monitor, out int localX, out int localY)
        {
            monitor = null;
            localX = localY = 0;
            _rw.EnterReadLock();
            try
            {
                // Use the lock-aware helper to avoid re-entering the reader lock inside FindMonitorAt.
                monitor = FindMonitorAtLocked(globalX, globalY);
                if (monitor == null) return false;

                localX = globalX - monitor.GlobalBounds.Left + monitor.LocalBounds.Left;
                localY = globalY - monitor.GlobalBounds.Top + monitor.LocalBounds.Top;
                return true;
            }
            finally { _rw.ExitReadLock(); }
        }

        // Translate a local coordinate (on a given monitor) into the global virtual space.
        public bool TranslateLocalToGlobal(string monitorId, int localX, int localY, out int globalX, out int globalY)
        {
            globalX = globalY = 0;
            _rw.EnterReadLock();
            try
            {
                if (!_monitors.TryGetValue(monitorId, out var m)) return false;
                globalX = m.GlobalBounds.Left + (localX - m.LocalBounds.Left);
                globalY = m.GlobalBounds.Top + (localY - m.LocalBounds.Top);
                return true;
            }
            finally { _rw.ExitReadLock(); }
        }

        // Find neighbor monitor that is adjacent in a direction; used for edge crossing decisions.
        // Direction: "left", "right", "up", "down" (simple axis search).
        public MonitorInfo? FindNeighbor(MonitorInfo subject, string direction)
        {
            if (subject == null) throw new ArgumentNullException(nameof(subject));
            _rw.EnterReadLock();
            try
            {
                MonitorInfo? best = null;
                foreach (var candidate in _monitors.Values)
                {
                    if (candidate.MonitorId == subject.MonitorId) continue;
                    switch (direction)
                    {
                        case "right":
                            // candidate should be to the right and vertically overlapping
                            if (candidate.GlobalBounds.Left >= subject.GlobalBounds.Right &&
                                RangesOverlap(candidate.GlobalBounds.Top, candidate.GlobalBounds.Bottom, subject.GlobalBounds.Top, subject.GlobalBounds.Bottom))
                            {
                                if (best == null || candidate.GlobalBounds.Left < best.GlobalBounds.Left) best = candidate;
                            }
                            break;
                        case "left":
                            if (candidate.GlobalBounds.Right <= subject.GlobalBounds.Left &&
                                RangesOverlap(candidate.GlobalBounds.Top, candidate.GlobalBounds.Bottom, subject.GlobalBounds.Top, subject.GlobalBounds.Bottom))
                            {
                                if (best == null || candidate.GlobalBounds.Right > best.GlobalBounds.Right) best = candidate;
                            }
                            break;
                        case "up":
                            if (candidate.GlobalBounds.Bottom <= subject.GlobalBounds.Top &&
                                RangesOverlap(candidate.GlobalBounds.Left, candidate.GlobalBounds.Right, subject.GlobalBounds.Left, subject.GlobalBounds.Right))
                            {
                                if (best == null || candidate.GlobalBounds.Bottom > best.GlobalBounds.Bottom) best = candidate;
                            }
                            break;
                        case "down":
                            if (candidate.GlobalBounds.Top >= subject.GlobalBounds.Bottom &&
                                RangesOverlap(candidate.GlobalBounds.Left, candidate.GlobalBounds.Right, subject.GlobalBounds.Left, subject.GlobalBounds.Right))
                            {
                                if (best == null || candidate.GlobalBounds.Top < best.GlobalBounds.Top) best = candidate;
                            }
                            break;
                        default:
                            throw new ArgumentException("Invalid direction");
                    }
                }
                return best;
            }
            finally { _rw.ExitReadLock(); }
        }

        private static bool RangesOverlap(int a1, int a2, int b1, int b2) =>
            Math.Max(a1, b1) < Math.Min(a2, b2);

        // Helper to call when a structural change happens.
        private void OnLayoutChangedLocked(bool suppressEvent = false)
        {
            // Rebuild spatial index immediately for consistency
            _spatialIndex.Rebuild(_monitors.Values);
            if (!suppressEvent)
            {
                // invoke outside lock to avoid reentrancy issues
                ThreadPool.QueueUserWorkItem(_ => LayoutChanged?.Invoke());
            }
        }

        // Expose read-only snapshot for diagnostics / UI
        public IReadOnlyList<ClientPc> GetClientsSnapshot()
        {
            _rw.EnterReadLock();
            try { return _clients.Values.Select(c => c).ToList(); }
            finally { _rw.ExitReadLock(); }
        }

        public IReadOnlyList<MonitorInfo> GetMonitorsSnapshot()
        {
            _rw.EnterReadLock();
            try { return _monitors.Values.Select(m => m).ToList(); }
            finally { _rw.ExitReadLock(); }
        }
    }
}
