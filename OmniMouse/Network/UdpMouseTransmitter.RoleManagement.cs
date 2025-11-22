using System;
using System.Net;

namespace OmniMouse.Network
{
    public partial class UdpMouseTransmitter
    {
        public void SetLocalRole(ConnectionRole role)
        {
            lock (_roleLock)
            {
                _currentRole = role;
                _handshakeComplete = true;
            }
            Console.WriteLine($"[UDP] Local role forced to {role}");
            RoleChanged?.Invoke(role);
        }

        private void HandleRoleSwitchRequest(IPEndPoint packetRemote)
        {
            if (_remoteEndPoint == null || !packetRemote.Address.Equals(_remoteEndPoint.Address))
            {
                Console.WriteLine($"[UDP][RoleSwitch][DROP] Request from unexpected {packetRemote.Address}");
                return;
            }

            ConnectionRole newRole;
            lock (_roleLock)
            {
                if (!_handshakeComplete) return;
                _currentRole = _currentRole == ConnectionRole.Sender
                    ? ConnectionRole.Receiver
                    : ConnectionRole.Sender;
                newRole = _currentRole;
            }

            Console.WriteLine($"[UDP][RoleSwitch] Role switched to {newRole}");
            RoleChanged?.Invoke(newRole);

            var buf = new byte[1];
            buf[0] = MSG_ROLE_SWITCH_ACCEPT;

            try
            {
                _udpClient?.Send(buf, buf.Length, _remoteEndPoint);
                Console.WriteLine($"[UDP][RoleSwitch] Accepted and confirmed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UDP][RoleSwitch] Failed to send acceptance: {ex.Message}");
            }
        }

        private void HandleRoleSwitchAccept(IPEndPoint packetRemote)
        {
            if (_remoteEndPoint == null || !packetRemote.Address.Equals(_remoteEndPoint.Address))
            {
                Console.WriteLine($"[UDP][RoleSwitch][DROP] Accept from unexpected {packetRemote.Address}");
                return;
            }

            ConnectionRole newRole;
            lock (_roleLock)
            {
                if (!_handshakeComplete) return;
                _currentRole = _currentRole == ConnectionRole.Sender
                    ? ConnectionRole.Receiver
                    : ConnectionRole.Sender;
                newRole = _currentRole;
            }

            Console.WriteLine($"[UDP][RoleSwitch] Confirmed. Now {newRole}");
            RoleChanged?.Invoke(newRole);
        }
    }
}