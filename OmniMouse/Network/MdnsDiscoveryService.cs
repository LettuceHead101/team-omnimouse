using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Makaretu.Dns;

namespace OmniMouse.Network
{
    /// <summary>
    /// Provides automatic peer discovery using mDNS (Multicast DNS) service advertisement and browsing.
    /// Allows OmniMouse instances to find each other on the local network without manual IP entry.
    /// </summary>
    public class MdnsDiscoveryService : IDisposable
    {
        private const string ServiceType = "_omnimouse._udp";
        private const int UdpPort = 5000; // Must match UdpMouseTransmitter port
        
        private readonly MulticastService _mdns;
        private readonly ServiceDiscovery _sd;
        private readonly string _localMachineId;
        private ServiceProfile? _advertisedProfile;
        private bool _isAdvertising;
        private bool _isBrowsing;
        private readonly HashSet<string> _discoveredPeerIds = new();
        
        /// <summary>
        /// Raised when a valid peer is discovered (not ourselves).
        /// Provides the peer's IP address and port.
        /// </summary>
        public event Action<IPEndPoint, string>? PeerDiscovered;
        
        /// <summary>
        /// Raised when a previously discovered peer is no longer available.
        /// </summary>
        public event Action<string>? PeerLost;
        
        /// <summary>
        /// Status messages for UI feedback (e.g., "Searching for peers...", "Found peer at 192.168.1.5").
        /// </summary>
        public event Action<string>? StatusChanged;

        public MdnsDiscoveryService()
        {
            _mdns = new MulticastService();
            _sd = new ServiceDiscovery(_mdns);
            
            // Generate a unique machine ID for self-filtering
            _localMachineId = GenerateMachineId();
            
            Console.WriteLine($"[mDNS] Service initialized with MachineId={_localMachineId}");
        }

        /// <summary>
        /// Starts advertising this machine's OmniMouse service and browsing for peers.
        /// </summary>
        public void Start()
        {
            if (_isAdvertising || _isBrowsing)
            {
                Console.WriteLine("[mDNS] Already started");
                return;
            }

            try
            {
                // Get local IPv4 address for advertising
                var localIp = GetLocalIPv4Address();
                if (localIp == null)
                {
                    StatusChanged?.Invoke("Failed to determine local IP address");
                    Console.WriteLine("[mDNS] Failed to get local IPv4 address");
                    return;
                }

                // Start the multicast service
                _mdns.Start();
                Console.WriteLine("[mDNS] Multicast service started");

                // Advertise our service
                StartAdvertising(localIp);
                
                // Browse for peer services
                StartBrowsing();
                
                StatusChanged?.Invoke("Searching for peers...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[mDNS] Failed to start: {ex.Message}");
                StatusChanged?.Invoke($"Discovery failed: {ex.Message}");
                Stop();
            }
        }

        /// <summary>
        /// Stops advertising and browsing.
        /// </summary>
        public void Stop()
        {
            if (_isBrowsing)
            {
                _sd.ServiceInstanceDiscovered -= OnServiceInstanceDiscovered;
                _sd.ServiceInstanceShutdown -= OnServiceInstanceShutdown;
                _isBrowsing = false;
            }

            if (_isAdvertising && _advertisedProfile != null)
            {
                _sd.Unadvertise(_advertisedProfile);
                _advertisedProfile = null;
                _isAdvertising = false;
            }

            try
            {
                _mdns.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[mDNS] Error stopping multicast service: {ex.Message}");
            }

            _discoveredPeerIds.Clear();
            Console.WriteLine("[mDNS] Service stopped");
        }

        private void StartAdvertising(IPAddress localIp)
        {
            var hostName = Environment.MachineName.ToLowerInvariant();
            var instanceName = $"omnimouse-{_localMachineId}";

            _advertisedProfile = new ServiceProfile(instanceName, ServiceType, UdpPort)
            {
                // Add TXT records for additional metadata
                Resources = new List<ResourceRecord>
                {
                    new TXTRecord
                    {
                        Name = $"{instanceName}.{ServiceType}.local",
                        Strings = new List<string>
                        {
                            $"machineid={_localMachineId}",
                            $"hostname={hostName}",
                            $"version=1.0"
                        }
                    },
                    new ARecord
                    {
                        Name = $"{instanceName}.{ServiceType}.local",
                        Address = localIp
                    }
                }
            };

            _sd.Advertise(_advertisedProfile);
            _isAdvertising = true;
            Console.WriteLine($"[mDNS] Advertising service: {instanceName} at {localIp}:{UdpPort}");
        }

        private void StartBrowsing()
        {
            _sd.ServiceInstanceDiscovered += OnServiceInstanceDiscovered;
            _sd.ServiceInstanceShutdown += OnServiceInstanceShutdown;
            _isBrowsing = true;
            
            // Query for existing services
            _mdns.SendQuery(ServiceType + ".local", type: DnsType.PTR);
            Console.WriteLine($"[mDNS] Browsing for service: {ServiceType}");
        }

        private void OnServiceInstanceDiscovered(object? sender, ServiceInstanceDiscoveryEventArgs e)
        {
            try
            {
                var serviceName = e.ServiceInstanceName.ToString();
                Console.WriteLine($"[mDNS] Discovered service instance: {serviceName}");

                // Extract machine ID from TXT records
                var txtRecords = e.Message.AdditionalRecords
                    .OfType<TXTRecord>()
                    .FirstOrDefault();

                string? peerMachineId = null;
                if (txtRecords != null)
                {
                    foreach (var txt in txtRecords.Strings)
                    {
                        if (txt.StartsWith("machineid="))
                        {
                            peerMachineId = txt.Substring("machineid=".Length);
                            break;
                        }
                    }
                }

                // Self-filter: ignore our own advertisement
                if (peerMachineId == _localMachineId)
                {
                    Console.WriteLine($"[mDNS] Ignoring self-advertisement (MachineId={peerMachineId})");
                    return;
                }

                if (string.IsNullOrEmpty(peerMachineId))
                {
                    Console.WriteLine($"[mDNS] No machine ID found in TXT records for {serviceName}");
                    peerMachineId = serviceName; // Fallback to service name
                }

                // Extract IP address from A records
                var aRecord = e.Message.AdditionalRecords
                    .OfType<ARecord>()
                    .FirstOrDefault();

                if (aRecord == null)
                {
                    Console.WriteLine($"[mDNS] No A record found for {serviceName}");
                    return;
                }

                var peerIp = aRecord.Address;
                var peerEndpoint = new IPEndPoint(peerIp, UdpPort);

                // Track discovered peer
                if (_discoveredPeerIds.Add(peerMachineId))
                {
                    Console.WriteLine($"[mDNS] *** PEER DISCOVERED *** MachineId={peerMachineId}, Endpoint={peerEndpoint}");
                    StatusChanged?.Invoke($"Found peer: {peerIp}");
                    PeerDiscovered?.Invoke(peerEndpoint, peerMachineId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[mDNS] Error processing discovered service: {ex.Message}");
            }
        }

        private void OnServiceInstanceShutdown(object? sender, ServiceInstanceShutdownEventArgs e)
        {
            try
            {
                var serviceName = e.ServiceInstanceName.ToString();
                Console.WriteLine($"[mDNS] Service instance shutdown: {serviceName}");

                // Extract machine ID to notify about peer loss
                var txtRecords = e.Message.AdditionalRecords
                    .OfType<TXTRecord>()
                    .FirstOrDefault();

                string? peerMachineId = null;
                if (txtRecords != null)
                {
                    foreach (var txt in txtRecords.Strings)
                    {
                        if (txt.StartsWith("machineid="))
                        {
                            peerMachineId = txt.Substring("machineid=".Length);
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(peerMachineId) && _discoveredPeerIds.Remove(peerMachineId))
                {
                    Console.WriteLine($"[mDNS] Peer lost: MachineId={peerMachineId}");
                    StatusChanged?.Invoke("Peer disconnected");
                    PeerLost?.Invoke(peerMachineId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[mDNS] Error processing service shutdown: {ex.Message}");
            }
        }

        private static IPAddress? GetLocalIPv4Address()
        {
            try
            {
                // Get all network interfaces
                var host = Dns.GetHostEntry(Dns.GetHostName());
                
                // Find first non-loopback IPv4 address
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        return ip;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[mDNS] Error getting local IP: {ex.Message}");
            }

            return null;
        }

        private static string GenerateMachineId()
        {
            // Create a stable machine ID based on machine name and MAC address
            try
            {
                var machineName = Environment.MachineName;
                var networkInterfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                
                // Get first physical MAC address
                var mac = networkInterfaces
                    .Where(ni => ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback 
                              && ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                    .Select(ni => ni.GetPhysicalAddress().ToString())
                    .FirstOrDefault();

                var combined = $"{machineName}-{mac}";
                
                // Create short hash for readability
                using var sha = System.Security.Cryptography.SHA256.Create();
                var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combined));
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 12).ToLowerInvariant();
            }
            catch
            {
                // Fallback to random GUID
                return Guid.NewGuid().ToString("N").Substring(0, 12);
            }
        }

        public void Dispose()
        {
            Stop();
            _mdns?.Dispose();
        }
    }
}
