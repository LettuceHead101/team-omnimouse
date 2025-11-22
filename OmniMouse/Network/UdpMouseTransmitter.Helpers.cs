using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace OmniMouse.Network
{
    public partial class UdpMouseTransmitter
    {
        private static bool TryParseIpv4OrResolve(string host, out IPAddress ip)
        {
            ip = IPAddress.Any;
            if (IPAddress.TryParse(host, out var parsed) && parsed.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(parsed))
            {
                ip = parsed;
                return true;
            }

            var resolved = Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
            if (resolved != null)
            {
                ip = resolved;
                return true;
            }

            return false;
        }

        private static IPAddress? GetLowestLocalIPv4()
        {
            try
            {
                return Dns.GetHostAddresses(Dns.GetHostName())
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) && !a.ToString().StartsWith("169.254."))
                    .OrderBy(a => a.GetAddressBytes(), LexicographicByteComparer.Instance)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetIPv4Bytes(IPAddress? ip, Span<byte> dest4)
        {
            if (ip == null) return false;
            var v4 = ip.AddressFamily == AddressFamily.InterNetwork ? ip : ip.MapToIPv4();
            var bytes = v4.GetAddressBytes();
            if (bytes.Length != 4) return false;
            bytes.CopyTo(dest4);
            return true;
        }

        private static IPAddress BytesToIPv4(ReadOnlySpan<byte> src4) =>
            new IPAddress(new byte[] { src4[0], src4[1], src4[2], src4[3] });

        private static int CompareIPv4(IPAddress a, IPAddress b)
        {
            var ba = a.MapToIPv4().GetAddressBytes();
            var bb = b.MapToIPv4().GetAddressBytes();
            return LexicographicByteComparer.Instance.Compare(ba, bb);
        }

        private sealed class LexicographicByteComparer : System.Collections.Generic.IComparer<byte[]>
        {
            public static readonly LexicographicByteComparer Instance = new();
            public int Compare(byte[]? x, byte[]? y)
            {
                if (x == y) return 0;
                if (x == null) return -1;
                if (y == null) return 1;
                var len = Math.Min(x.Length, y.Length);
                for (int i = 0; i < len; i++)
                {
                    int c = x[i].CompareTo(y[i]);
                    if (c != 0) return c;
                }
                return x.Length.CompareTo(y.Length);
            }
        }
    }
}