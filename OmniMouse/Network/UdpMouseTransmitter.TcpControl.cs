using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using OmniMouse.Hooks;
using OmniMouse.Switching;

namespace OmniMouse.Network
{
    public partial class UdpMouseTransmitter
    {
        private TcpListener? _tcpListener;
        private Thread? _tcpAcceptThread;

        private void StartTcpControlListener()
        {
            try
            {
                if (_tcpListener != null) return;

                _tcpListener = new TcpListener(new IPEndPoint(IPAddress.Any, TcpControlPort));
                _tcpListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _tcpListener.Start();
                var local = _tcpListener.LocalEndpoint as IPEndPoint;
                Console.WriteLine($"[TCP][Listen] Bound {local?.Address}:{local?.Port}");

                _tcpAcceptThread = new Thread(TcpAcceptLoop)
                {
                    IsBackground = true,
                    Name = "UdpMouseTransmitter.TcpAcceptLoop"
                };
                _tcpAcceptThread.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TCP][Listen] Failed to start listener: {ex.Message}");
                try { _tcpListener?.Stop(); } catch { }
                _tcpListener = null;
            }
        }

        private void StopTcpControlListener()
        {
            try
            {
                _tcpListener?.Stop();
            }
            catch { }
            finally
            {
                _tcpListener = null;
            }

            try
            {
                if (_tcpAcceptThread != null && _tcpAcceptThread.IsAlive)
                {
                    if (!_tcpAcceptThread.Join(500)) { /* let it unwind */ }
                }
            }
            catch { }
            finally
            {
                _tcpAcceptThread = null;
            }
        }

        private void TcpAcceptLoop()
        {
            Console.WriteLine("[TCP][Listen] Accept loop started");
            try
            {
                while (_tcpListener != null)
                {
                    TcpClient? client = null;
                    try
                    {
                        client = _tcpListener.AcceptTcpClient();
                        client.ReceiveTimeout = 5000;
                        client.SendTimeout = 5000;

                        var remote = client.Client.RemoteEndPoint as IPEndPoint;
                        var remoteAddr = remote?.Address.MapToIPv4();
                        if (_remoteEndPoint != null && !Equals(remoteAddr, _remoteEndPoint.Address))
                        {
                            Console.WriteLine($"[TCP][Listen][DROP] unexpected {remoteAddr} (expected {_remoteEndPoint.Address})");
                            try { client.Close(); } catch { }
                            continue;
                        }

                        using var ns = client.GetStream();

                        // Expect exactly 1 + 4 + 4 bytes
                        var header = new byte[1];
                        int r = ReadExact(ns, header, 0, 1);
                        if (r != 1)
                        {
                            Console.WriteLine("[TCP][Listen] short read on header");
                            continue;
                        }

                        if (header[0] != MSG_TAKE_CONTROL_AT)
                        {
                            Console.WriteLine($"[TCP][Listen] unexpected opcode 0x{header[0]:X2}");
                            continue;
                        }

                        var payload = new byte[8];
                        r = ReadExact(ns, payload, 0, 8);
                        if (r != 8)
                        {
                            Console.WriteLine("[TCP][Listen] short read on payload");
                            continue;
                        }

                        int ux = BitConverter.ToInt32(payload, 0);
                        int uy = BitConverter.ToInt32(payload, 4);
                        Console.WriteLine($"[TCP][TakeControl] Received UNIVERSAL ({ux},{uy}) from {remoteAddr}");

                        // Map universal (0..65535) to local pixel coordinates using primary bounds
                        try
                        {
                            var topology = new Win32ScreenTopology();
                            var bounds = topology.GetScreenConfiguration();
                            var mapper = new DefaultCoordinateMapper();
                            var refBounds = mapper.GetReferenceBounds(isRelativeMode: false, isController: false,
                                desktopBounds: bounds.DesktopBounds, primaryBounds: bounds.PrimaryScreenBounds);
                            var pixel = mapper.MapToPixel(new System.Drawing.Point(ux, uy), refBounds);

                            // End remote streaming since we're receiving control
                            InputHooks.EndRemoteStreaming();
                            
                            InputHooks.SuppressNextMoveFrom(pixel.X, pixel.Y);
                            SetCursorPos(pixel.X, pixel.Y);
                            try { TakeControlReceived?.Invoke(pixel.X, pixel.Y); } catch (Exception ex) { Console.WriteLine($"[TCP][Listen] handler error: {ex.Message}"); }
                        }
                        catch (Exception mapEx)
                        {
                            Console.WriteLine($"[TCP][TakeControl] Mapping failed: {mapEx.Message}");
                        }

                        // Send ACK
                        var ack = new byte[] { MSG_TAKE_CONTROL_ACK };
                        ns.Write(ack, 0, 1);
                        ns.Flush();
                    }
                    catch (SocketException ex)
                    {
                        // Likely listener stopped or transient error
                        if (_tcpListener == null) break;
                        Console.WriteLine($"[TCP][Listen] Socket error: {ex.Message}");
                    }
                    catch (ObjectDisposedException)
                    {
                        break; // listener stopped
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[TCP][Listen] Unexpected error: {ex.Message}");
                    }
                    finally
                    {
                        try { client?.Close(); } catch { }
                    }
                }
            }
            finally
            {
                Console.WriteLine("[TCP][Listen] Accept loop exited");
            }
        }

        private static int ReadExact(System.IO.Stream s, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int r = 0;
                try { r = s.Read(buffer, offset + total, count - total); }
                catch (System.IO.IOException) { return total; }
                if (r <= 0) break;
                total += r;
            }
            return total;
        }
    }
}
