using System;
using System.Net;
using System.Security.Cryptography;
using System.Threading;

namespace OmniMouse.Network
{
    public partial class UdpMouseTransmitter
    {
        private void BeginHandshake()
        {
            CancelHandshakeTimer();

            _hsAttempts = 0;
            _hsNonceLocal = RandomNonce();
            _hsNoncePeer = 0;
            _hsInProgress = true;

            Console.WriteLine($"[UDP][Handshake] Begin v{ProtocolVersion}, nonce={_hsNonceLocal}, localId={_localLowestIpV4}");

            _hsTimer = new Timer(HandshakeTimerCallback, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }

        private void HandshakeTimerCallback(object? _)
        {
            try
            {
                if (_udpClient == null || _remoteEndPoint == null)
                {
                    Console.WriteLine("[UDP][Handshake] No UDP or remote endpoint; stopping retries.");
                    CancelHandshakeTimer();
                    return;
                }

                bool done;
                lock (_roleLock) done = _handshakeComplete;
                if (done)
                {
                    CancelHandshakeTimer();
                    return;
                }

                if (!_hsInProgress)
                {
                    CancelHandshakeTimer();
                    return;
                }

                _hsAttempts++;
                SendHandshakeRequest();

                if (_hsAttempts >= 10)
                {
                    Console.WriteLine("[UDP][Handshake] Max attempts reached; giving up for now.");
                    CancelHandshakeTimer();
                    return;
                }

                var delayMs = Math.Min(2000, 250 * (1 << Math.Min(6, _hsAttempts - 1)));
                _hsTimer?.Change(TimeSpan.FromMilliseconds(delayMs), Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // shutting down
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UDP][Handshake] Timer error: {ex.Message}");
            }
        }

        private void CancelHandshakeTimer()
        {
            _hsInProgress = false;
            try { _hsTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
            try { _hsTimer?.Dispose(); } catch { }
            _hsTimer = null;
        }

        private void SendHandshakeRequest()
        {
            if (_udpClient == null || _remoteEndPoint == null) return;

            ConnectionRole prefRole;
            lock (_roleLock) prefRole = _currentRole;

            Span<byte> buf = stackalloc byte[15];
            int o = 0;
            buf[o++] = MSG_HANDSHAKE_REQUEST;
            buf[o++] = ProtocolVersion;
            BitConverter.TryWriteBytes(buf.Slice(2, 8), _hsNonceLocal);
            buf[o = 10] = (byte)prefRole;
            o++;
            if (!TryGetIPv4Bytes(_localLowestIpV4, buf.Slice(o, 4)))
            {
                buf[o++] = 0; buf[o++] = 0; buf[o++] = 0; buf[o++] = 0;
            }
            else
            {
                o += 4;
            }

            try
            {
                _udpClient.Send(buf.ToArray(), o, _remoteEndPoint);
                Console.WriteLine($"[UDP][Handshake] -> Request v{ProtocolVersion} nonce={_hsNonceLocal}, prefRole={prefRole}, localId={_localLowestIpV4}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UDP][Handshake] Failed to send request: {ex.Message}");
            }
        }

        private void HandleHandshakeRequest(byte[] data, IPEndPoint packetRemote)
        {
            if (data.Length < 15) return;
            var version = data[1];
            if (version != ProtocolVersion)
            {
                Console.WriteLine($"[UDP][Handshake][WARN] Version mismatch {version}!={ProtocolVersion} from {packetRemote.Address}; ignoring.");
                return;
            }

            var nonceA = BitConverter.ToInt64(data, 2);
            var initiatorPrefRole = (ConnectionRole)data[10];
            var initiatorLocalIp = BytesToIPv4(new ReadOnlySpan<byte>(data, 11, 4));

            bool drop = false;
            bool learnedEndpoint = false;
            IPEndPoint? sendEndpoint = null;
            var localId = _localLowestIpV4 ?? IPAddress.Parse("0.0.0.0");
            var negotiatedLocalRole = ConnectionRole.Receiver;

            lock (_roleLock)
            {
                if (_remoteEndPoint != null && !packetRemote.Address.Equals(_remoteEndPoint.Address))
                {
                    Console.WriteLine($"[UDP][Handshake][DROP] Request from unexpected {packetRemote.Address}; expected {_remoteEndPoint.Address}");
                    drop = true;
                }
                else
                {
                    if (_remoteEndPoint == null)
                    {
                        _remoteEndPoint = new IPEndPoint(packetRemote.Address.MapToIPv4(), UdpPort);
                        learnedEndpoint = true;
                        Console.WriteLine($"[UDP] Remote endpoint learned: {_remoteEndPoint.Address}:{_remoteEndPoint.Port}");
                    }

                    // Seamless mode: initial handshake no longer assigns Sender based on IP.
                    // All parties start as Receiver; first edge transition will claim Sender locally.
                    negotiatedLocalRole = ConnectionRole.Receiver;

                    sendEndpoint = _remoteEndPoint;
                }
            }

            if (drop) return;
            if (sendEndpoint == null) return;

            Console.WriteLine($"[UDP][Handshake] Request from {packetRemote.Address} v{version} nonceA={nonceA}, peerPref={initiatorPrefRole}, peerId={initiatorLocalIp} -> negotiated localRole={negotiatedLocalRole}");

            _hsNoncePeer = RandomNonce();
            var accept = new byte[27];
            int o = 0;
            accept[o++] = MSG_HANDSHAKE_ACCEPT;
            accept[o++] = ProtocolVersion;
            Array.Copy(BitConverter.GetBytes(nonceA), 0, accept, o, 8); o += 8;
            Array.Copy(BitConverter.GetBytes(_hsNoncePeer), 0, accept, o, 8); o += 8;
            accept[o++] = (byte)negotiatedLocalRole;
            if (!TryGetIPv4Bytes(localId, accept.AsSpan(o, 4)))
            {
                accept[o++] = 0; accept[o++] = 0; accept[o++] = 0; accept[o++] = 0;
            }
            else { o += 4; }
            accept[o++] = data[11]; accept[o++] = data[12]; accept[o++] = data[13]; accept[o++] = data[14];

            try
            {
                _udpClient?.Send(accept, o, sendEndpoint);
                Console.WriteLine($"[UDP][Handshake] <- Accept v{ProtocolVersion} echo={nonceA}, nonceB={_hsNoncePeer}, responderRole={negotiatedLocalRole}, responderId={localId}, initiatorIdEcho={initiatorLocalIp}");

                lock (_roleLock)
                {
                    _currentRole = negotiatedLocalRole;
                    _handshakeComplete = true;
                }

                // Initialize layout coordinator after handshake completes
                var localEp = _udpClient?.Client.LocalEndPoint as IPEndPoint;
                var localMachineId = $"{localEp?.Address}:{localEp?.Port}";
                var remoteMachineId = $"{sendEndpoint.Address}:{sendEndpoint.Port}";
                InitializeLayoutCoordinator(localMachineId, remoteMachineId);

                RoleChanged?.Invoke(negotiatedLocalRole);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UDP][Handshake] Failed to send acceptance: {ex.Message}");

                if (learnedEndpoint)
                {
                    lock (_roleLock)
                    {
                        if (_remoteEndPoint != null && _remoteEndPoint.Address.Equals(sendEndpoint.Address))
                        {
                            _remoteEndPoint = null;
                            Console.WriteLine("[UDP][Handshake] Cleared learned remote endpoint due to send failure.");
                        }
                    }
                }
            }
        }

        private void HandleHandshakeAccept(byte[] data, IPEndPoint packetRemote)
        {
            if (data.Length < 27) return;
            var version = data[1];
            if (version != ProtocolVersion)
            {
                Console.WriteLine($"[UDP][Handshake][WARN] Accept version mismatch {version}!={ProtocolVersion} from {packetRemote.Address}; ignoring.");
                return;
            }

            if (_remoteEndPoint != null && !packetRemote.Address.Equals(_remoteEndPoint.Address))
            {
                Console.WriteLine($"[UDP][Handshake][DROP] Accept from unexpected {packetRemote.Address}; expected {_remoteEndPoint.Address}");
                return;
            }

            var nonceEcho = BitConverter.ToInt64(data, 2);
            if (nonceEcho != _hsNonceLocal)
            {
                Console.WriteLine($"[UDP][Handshake][DROP] Accept nonce mismatch echo={nonceEcho} expected={_hsNonceLocal} from {packetRemote.Address}");
                return;
            }

            _hsNoncePeer = BitConverter.ToInt64(data, 10);
            var responderRole = (ConnectionRole)data[18];
            var responderLocalIp = BytesToIPv4(new ReadOnlySpan<byte>(data, 19, 4));
            var initiatorLocalIpEcho = BytesToIPv4(new ReadOnlySpan<byte>(data, 23, 4));

            if (_remoteEndPoint == null)
            {
                _remoteEndPoint = new IPEndPoint(packetRemote.Address.MapToIPv4(), UdpPort);
                Console.WriteLine($"[UDP] Remote endpoint learned on accept: {_remoteEndPoint.Address}:{_remoteEndPoint.Port}");
            }

            var localId = _localLowestIpV4 ?? IPAddress.Parse("0.0.0.0");
            // Seamless mode accept: keep local role Receiver. Role claim happens via edge detection.
            var negotiatedLocalRole = ConnectionRole.Receiver;

            lock (_roleLock)
            {
                _currentRole = negotiatedLocalRole;
                _handshakeComplete = true;
            }

            Console.WriteLine($"[UDP][Handshake] Accept from {packetRemote.Address} v{version} echo={nonceEcho}, nonceB={_hsNoncePeer}, responderRole={responderRole}, responderId={responderLocalIp}, initiatorIdEcho={initiatorLocalIpEcho} -> negotiated localRole={negotiatedLocalRole}");

            CancelHandshakeTimer();

            // Initialize layout coordinator after handshake completes
            var localEp = _udpClient?.Client.LocalEndPoint as IPEndPoint;
            var localMachineId = $"{localEp?.Address}:{localEp?.Port}";
            var remoteMachineId = $"{_remoteEndPoint.Address}:{_remoteEndPoint.Port}";
            InitializeLayoutCoordinator(localMachineId, remoteMachineId);

            RoleChanged?.Invoke(negotiatedLocalRole);
        }

        private static long RandomNonce()
        {
            Span<byte> b = stackalloc byte[8];
            RandomNumberGenerator.Fill(b);
            return BitConverter.ToInt64(b);
        }
    }
}