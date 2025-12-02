using System;
using System.Net;
using System.Threading;
using OmniMouse.Hooks;
using System.Net.Sockets;
using System.IO;

namespace OmniMouse.Network
{
    public partial class UdpMouseTransmitter
    {
        // Test seam: allow tests to override the SendTakeControl behavior to avoid real TCP sockets.
        // If non-null, this delegate will be invoked instead of performing the real TCP handshake/send.
        internal Action<string, int, int>? SendTakeControlImpl;

        /// <summary>
        /// When sending relative deltas, encodes them by adding/subtracting MOVE_MOUSE_RELATIVE.
        /// Receiver detects |X| >= 100000 && |Y| >= 100000 to know it's relative.
        /// </summary>
        /// <param name="x">Absolute X coord OR delta-X (will be encoded with sentinel if isDelta=true)</param>
        /// <param name="y">Absolute Y coord OR delta-Y (will be encoded with sentinel if isDelta=true)</param>
        /// <param name="isDelta">True to encode as relative delta with sentinel, false for absolute</param>
        public void SendMouse(int x, int y, bool isDelta = false)
        {
            lock (_roleLock)
            {
                if (!_handshakeComplete || _currentRole != ConnectionRole.Sender)
                    return;
            }

            if (_udpClient == null || _remoteEndPoint == null) return;

            int encodedX = x;
            int encodedY = y;

            if (isDelta)
            {
                encodedX = x + (x < 0 ? -MOVE_MOUSE_RELATIVE : MOVE_MOUSE_RELATIVE);
                encodedY = y + (y < 0 ? -MOVE_MOUSE_RELATIVE : MOVE_MOUSE_RELATIVE);
                Console.WriteLine($"[UDP][SendMouse] Delta ({x},{y}) encoded as ({encodedX},{encodedY})");
            }
            else
            {
                Console.WriteLine($"[UDP][SendMouse] Absolute ({x},{y})");
            }

            var buf = new byte[1 + 8];
            buf[0] = MSG_MOUSE;
            Array.Copy(BitConverter.GetBytes(encodedX), 0, buf, 1, 4);
            Array.Copy(BitConverter.GetBytes(encodedY), 0, buf, 1 + 4, 4);
            _udpClient.Send(buf, buf.Length, _remoteEndPoint);
        }

        /// <summary>
        /// Legacy wrapper for backward compatibility. Sends absolute mouse position.
        /// </summary>
        public void SendMousePosition(int x, int y)
        {
            SendMouse(x, y, isDelta: false);
        }

        /// <summary>
        /// Sends a mouse button event (down/up) to the remote peer.
        /// </summary>
        public void SendMouseButton(MouseButtonNet button, bool isDown)
        {
            lock (_roleLock)
            {
                if (!_handshakeComplete || _currentRole != ConnectionRole.Sender)
                    return;
            }
            if (_udpClient == null || _remoteEndPoint == null) return;

            var buf = new byte[1 + 2];
            buf[0] = MSG_MOUSE_BUTTON;
            buf[1] = (byte)button;
            buf[2] = (byte)(isDown ? 1 : 0);
            _udpClient.Send(buf, buf.Length, _remoteEndPoint);
            Console.WriteLine($"[UDP][SendBtn] {button} {(isDown ? "DOWN" : "UP")}");
        }

        /// <summary>
        /// Sends a mouse wheel delta to the remote peer.
        /// </summary>
        public void SendMouseWheel(int delta)
        {
            lock (_roleLock)
            {
                if (!_handshakeComplete || _currentRole != ConnectionRole.Sender)
                    return;
            }
            if (_udpClient == null || _remoteEndPoint == null) return;

            var buf = new byte[1 + 4];
            buf[0] = MSG_MOUSE_WHEEL;
            Array.Copy(BitConverter.GetBytes(delta), 0, buf, 1, 4);
            _udpClient.Send(buf, buf.Length, _remoteEndPoint);
            Console.WriteLine($"[UDP][SendWheel] delta={delta}");
        }

        public void SendTakeControl(string targetClientId, int localX, int localY)
        {
            // If a test has installed a seam, call it and return early to avoid TCP I/O.
            var impl = SendTakeControlImpl;
            if (impl != null)
            {
                impl(targetClientId, localX, localY);
                return;
            }

            if (string.IsNullOrEmpty(targetClientId)) throw new ArgumentNullException(nameof(targetClientId));
            
            IPEndPoint? targetEndPoint = null;
            lock (_clientEndpoints)
            {
                if (_clientEndpoints.TryGetValue(targetClientId, out var ep))
                    targetEndPoint = ep;
            }

            if (targetEndPoint == null)
            {
                if (_remoteEndPoint == null)
                {
                    Console.WriteLine($"[TCP][TakeControl] Cannot send: unknown endpoint for {targetClientId} and no learned remote endpoint.");
                    throw new InvalidOperationException($"Cannot send take control: unknown endpoint for {targetClientId}");
                }
                targetEndPoint = _remoteEndPoint;
            }

            // Create TCP endpoint (using different port for control messages)
            var tcpEndPoint = new IPEndPoint(targetEndPoint.Address, TcpControlPort);
            TcpClient? tcpClient = null;
            NetworkStream? stream = null;

            try
            {
                // Get or create TCP connection
                lock (_tcpLock)
                {
                    if (_tcpConnections.TryGetValue(targetClientId, out var existingClient))
                    {
                        // Check if existing connection is still valid
                        if (existingClient.Connected)
                        {
                            tcpClient = existingClient;
                            Console.WriteLine($"[TCP][TakeControl] Reusing existing connection to {targetClientId}");
                        }
                        else
                        {
                            // Clean up dead connection
                            try { existingClient.Close(); } catch { }
                            _tcpConnections.Remove(targetClientId);
                        }
                    }
                }

                // Establish new connection if needed
                if (tcpClient == null)
                {
                    Console.WriteLine($"[TCP][TakeControl] Establishing new connection to {targetClientId} ({tcpEndPoint.Address}:{tcpEndPoint.Port})");
                    tcpClient = new TcpClient(AddressFamily.InterNetwork);

                    // Connect with timeout and a single retry
                    bool connected = false;
                    Exception? lastError = null;
                    for (int attempt = 1; attempt <= 2 && !connected; attempt++)
                    {
                        try
                        {
                            var connectTask = tcpClient.ConnectAsync(tcpEndPoint.Address, tcpEndPoint.Port);
                            if (!connectTask.Wait(TimeSpan.FromSeconds(attempt == 1 ? 3 : 6)))
                            {
                                throw new SocketException((int)SocketError.TimedOut);
                            }
                            connected = true;
                        }
                        catch (Exception ex)
                        {
                            lastError = ex;
                            try { tcpClient.Close(); } catch { }
                            if (attempt < 2)
                            {
                                Console.WriteLine($"[TCP][TakeControl] Connect attempt {attempt} failed ({ex.Message}), retrying...");
                                Thread.Sleep(250);
                                tcpClient = new TcpClient(AddressFamily.InterNetwork);
                            }
                        }
                    }

                    if (!connected)
                    {
                        throw new SocketException((int)SocketError.HostUnreachable);
                    }
                    
                    lock (_tcpLock)
                    {
                        _tcpConnections[targetClientId] = tcpClient;
                    }
                }

                stream = tcpClient.GetStream();
                
                // Build the take-control packet
                var buf = new byte[1 + 4 + 4];
                buf[0] = MSG_TAKE_CONTROL_AT;
                BitConverter.TryWriteBytes(buf.AsSpan(1, 4), localX);
                BitConverter.TryWriteBytes(buf.AsSpan(5, 4), localY);

                // Send the packet
                stream.WriteTimeout = 5000;
                stream.Write(buf, 0, buf.Length);
                stream.Flush();
                Console.WriteLine($"[TCP][TakeControl] Sent take-control to {targetClientId} ({tcpEndPoint.Address}) at ({localX},{localY})");

                // Wait for acknowledgment with timeout
                stream.ReadTimeout = 5000; // 5 seconds timeout
                var ackBuffer = new byte[1];
                
                var bytesRead = stream.Read(ackBuffer, 0, 1);
                
                if (bytesRead == 0)
                {
                    throw new IOException("Connection closed by remote host while waiting for acknowledgment");
                }

                if (ackBuffer[0] != MSG_TAKE_CONTROL_ACK)
                {
                    throw new InvalidOperationException($"Unexpected response: expected ACK (0x{MSG_TAKE_CONTROL_ACK:X2}), received 0x{ackBuffer[0]:X2}");
                }

                Console.WriteLine($"[TCP][TakeControl] Received acknowledgment from {targetClientId}");

                // We are now the active Sender that forwards local input to the target.
                // Keep hooks active and enable send-path by setting local role to Sender.
                this.SetLocalRole(ConnectionRole.Sender);
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"[TCP][TakeControl] Socket error: {ex.Message}");
                
                // Clean up failed connection
                lock (_tcpLock)
                {
                    _tcpConnections.Remove(targetClientId);
                }
                
                try { tcpClient?.Close(); } catch { }
                Console.WriteLine("[TCP][TakeControl] Hints: ensure peer is running, TCP 5001 is listening, and firewall allows inbound 5001. Try: Test-NetConnection -ComputerName {0} -Port {1}", tcpEndPoint.Address, tcpEndPoint.Port);
                throw new InvalidOperationException($"Failed to establish TCP connection to {targetClientId} ({tcpEndPoint.Address}:{tcpEndPoint.Port}): {ex.Message}", ex);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"[TCP][TakeControl] IO error: {ex.Message}");
                
                // Clean up failed connection
                lock (_tcpLock)
                {
                    _tcpConnections.Remove(targetClientId);
                }
                
                try { tcpClient?.Close(); } catch { }
                throw new InvalidOperationException($"Failed to send take control or receive acknowledgment from {targetClientId}: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TCP][TakeControl] Unexpected error: {ex.Message}");
                
                // Clean up on any other error
                lock (_tcpLock)
                {
                    _tcpConnections.Remove(targetClientId);
                }
                
                try { tcpClient?.Close(); } catch { }
                throw;
            }
        }

        private void ReceiveMouseLoopUDP()
        {
            Console.WriteLine("[UDP][RecvLoop] Enter");
            try
            {
                if (_udpClient == null) return;
                var remoteEP = new IPEndPoint(IPAddress.Any, 0);
                var lastHeartbeat = DateTime.UtcNow;

                while (_running)
                {
                    try
                    {
                        var data = _udpClient.Receive(ref remoteEP);
                        _recvLoopLastAlive = DateTime.UtcNow;
                        Interlocked.Increment(ref _recvLoopIterations);

                        if ((DateTime.UtcNow - lastHeartbeat) >= TimeSpan.FromSeconds(5))
                        {
                            var iters = Interlocked.Read(ref _recvLoopIterations);
                            Console.WriteLine($"[UDP][RecvLoop] alive. iterations={iters}, lastAlive={_recvLoopLastAlive:O}");
                            lastHeartbeat = DateTime.UtcNow;
                        }

                        if (data == null || data.Length == 0) continue;

                        if (_remoteEndPoint == null)
                        {
                            // Defer endpoint learning to handlers
                        }
                        else
                        {
                            if (!remoteEP.Address.Equals(_remoteEndPoint.Address))
                            {
                                continue;
                            }
                        }

                        switch (data[0])
                        {
                            case MSG_HANDSHAKE_REQUEST:
                                HandleHandshakeRequest(data, remoteEP);
                                break;

                            case MSG_HANDSHAKE_ACCEPT:
                                HandleHandshakeAccept(data, remoteEP);
                                break;

                            case MSG_ROLE_SWITCH_REQUEST:
                                HandleRoleSwitchRequest(remoteEP);
                                break;

                            case MSG_ROLE_SWITCH_ACCEPT:
                                HandleRoleSwitchAccept(remoteEP);
                                break;

                            case MSG_NORMALIZED_MOUSE:
                                lock (_roleLock)
                                {
                                    if (!_handshakeComplete || _currentRole != ConnectionRole.Receiver)
                                        break;
                                }

                                if (_remoteEndPoint != null && !remoteEP.Address.Equals(_remoteEndPoint.Address))
                                    break;

                                if (data.Length >= 1 + 8)
                                {
                                    var nx = BitConverter.ToSingle(data, 1);
                                    var ny = BitConverter.ToSingle(data, 1 + 4);

                                    Console.WriteLine($"[UDP][RecvNormalized] from {remoteEP.Address}:{remoteEP.Port} nx={nx:F6}, ny={ny:F6} (legacy normalized packet - not used with bitmap approach)");
                                }
                                break;

                            case MSG_MOUSE:
                                lock (_roleLock)
                                {
                                    if (!_handshakeComplete || _currentRole != ConnectionRole.Receiver)
                                        break;
                                }

                                if (_remoteEndPoint != null && !remoteEP.Address.Equals(_remoteEndPoint.Address))
                                    break;

                                if (data.Length >= 1 + 8)
                                {
                                    var x = BitConverter.ToInt32(data, 1);
                                    var y = BitConverter.ToInt32(data, 1 + 4);

                                    if (Math.Abs(x) >= MOVE_MOUSE_RELATIVE && Math.Abs(y) >= MOVE_MOUSE_RELATIVE)
                                    {
                                        // Relative delta: decode by removing sentinel
                                        int deltaX = x < 0 ? x + MOVE_MOUSE_RELATIVE : x - MOVE_MOUSE_RELATIVE;
                                        int deltaY = y < 0 ? y + MOVE_MOUSE_RELATIVE : y - MOVE_MOUSE_RELATIVE;
                                        Console.WriteLine($"[UDP][RecvMouse] Relative delta ({deltaX},{deltaY}) from encoded ({x},{y})");
                                        
                                        // Inject as relative movement
                                        InputHooks.InjectMouseDelta(deltaX, deltaY);
                                    }
                                    else
                                    {
                                        // Absolute position
                                        Console.WriteLine($"[UDP][RecvMouse] Absolute ({x},{y})");
                                        InputHooks.SuppressNextMoveFrom(x, y);
                                        SetCursorPos(x, y);
                                    }
                                }
                                break;

                            case MSG_MOUSE_BUTTON:
                                lock (_roleLock)
                                {
                                    if (!_handshakeComplete || _currentRole != ConnectionRole.Receiver)
                                        break;
                                }
                                if (_remoteEndPoint != null && !remoteEP.Address.Equals(_remoteEndPoint.Address))
                                    break;
                                if (data.Length >= 1 + 2)
                                {
                                    var button = (MouseButtonNet)data[1];
                                    bool isDown = data[2] != 0;
                                    Console.WriteLine($"[UDP][RecvBtn] {button} {(isDown ? "DOWN" : "UP")}");
                                    InputHooks.InjectMouseButton(button, isDown);
                                }
                                break;

                            case MSG_MOUSE_WHEEL:
                                lock (_roleLock)
                                {
                                    if (!_handshakeComplete || _currentRole != ConnectionRole.Receiver)
                                        break;
                                }
                                if (_remoteEndPoint != null && !remoteEP.Address.Equals(_remoteEndPoint.Address))
                                    break;
                                if (data.Length >= 1 + 4)
                                {
                                    int delta = BitConverter.ToInt32(data, 1);
                                    Console.WriteLine($"[UDP][RecvWheel] delta={delta}");
                                    InputHooks.InjectMouseWheel(delta);
                                }
                                break;

                            case MSG_LEGACY_MOUSE:
                                // Keep for backward compatibility during transition
                                lock (_roleLock)
                                {
                                    if (!_handshakeComplete || _currentRole != ConnectionRole.Receiver)
                                        break;
                                }

                                if (_remoteEndPoint != null && !remoteEP.Address.Equals(_remoteEndPoint.Address))
                                    break;

                                if (data.Length >= 1 + 8)
                                {
                                    var x = BitConverter.ToInt32(data, 1);
                                    var y = BitConverter.ToInt32(data, 1 + 4);
                                    Console.WriteLine($"[UDP][RecvLegacy] from {remoteEP.Address}:{remoteEP.Port} -> ({x},{y})");
                                    InputHooks.SuppressNextMoveFrom(x, y);
                                    SetCursorPos(x, y);
                                }
                                break;

                            case MSG_TAKE_CONTROL_AT:
                                if (_remoteEndPoint != null && !remoteEP.Address.Equals(_remoteEndPoint.Address))
                                {
                                    Console.WriteLine($"[UDP][TakeControl][DROP] from unexpected {remoteEP.Address}");
                                    break;
                                }

                                if (data.Length >= 1 + 8)
                                {
                                    var ux = BitConverter.ToInt32(data, 1);
                                    var uy = BitConverter.ToInt32(data, 1 + 4);
                                    Console.WriteLine($"[UDP][TakeControl] Received UNIVERSAL ({ux},{uy}) from {remoteEP.Address}:{remoteEP.Port}");

                                    try
                                    {
                                        var topology = new OmniMouse.Switching.Win32ScreenTopology();
                                        var bounds = topology.GetScreenConfiguration();
                                        var mapper = new OmniMouse.Switching.DefaultCoordinateMapper();
                                        var refBounds = bounds.DesktopBounds; // Use full desktop instead of primary screen only
                                        var pixel = mapper.MapToPixel(new System.Drawing.Point(ux, uy), refBounds);

                                        // End remote streaming since we're receiving control
                                        InputHooks.EndRemoteStreaming();
                                        
                                        InputHooks.SuppressNextMoveFrom(pixel.X, pixel.Y);
                                        SetCursorPos(pixel.X, pixel.Y);
                                        try { TakeControlReceived?.Invoke(pixel.X, pixel.Y); } catch (Exception ex) { Console.WriteLine($"[UDP][TakeControl] handler error: {ex.Message}"); }
                                    }
                                    catch (Exception mapEx)
                                    {
                                        Console.WriteLine($"[UDP][TakeControl] Mapping failed: {mapEx.Message}");
                                    }
                                }
                                break;

                            case MSG_LAYOUT_UPDATE:
                                if (_remoteEndPoint != null && !remoteEP.Address.Equals(_remoteEndPoint.Address))
                                {
                                    Console.WriteLine($"[UDP][LayoutUpdate][DROP] from unexpected {remoteEP.Address}");
                                    break;
                                }

                                if (data.Length > 1)
                                {
                                    HandleLayoutUpdate(data, 1);
                                }
                                break;

                            case MSG_KEYBOARD_DOWN:
                                HandleKeyboardDown(data, remoteEP);
                                break;

                            case MSG_KEYBOARD_UP:
                                HandleKeyboardUp(data, remoteEP);
                                break;

                            case MSG_MONITOR_INFO:
                                if (_remoteEndPoint != null && !remoteEP.Address.Equals(_remoteEndPoint.Address))
                                {
                                    Console.WriteLine($"[UDP][MonitorInfo][DROP] from unexpected {remoteEP.Address}");
                                    break;
                                }

                                if (data.Length > 1)
                                {
                                    HandleMonitorInfo(data, 1);
                                }
                                break;

                            default:
                                if (_remoteEndPoint != null && !remoteEP.Address.Equals(_remoteEndPoint.Address))
                                    break;

                                if (data.Length == 8)
                                {
                                    var nx = BitConverter.ToSingle(data, 0);
                                    var ny = BitConverter.ToSingle(data, 4);
                                    if (nx >= 0f && nx <= 1f && ny >= 0f && ny <= 1f)
                                    {
                                        Console.WriteLine($"[UDP][RecvFallbackFloat] nx={nx:F6}, ny={ny:F6} (legacy - bitmap approach uses MSG_TAKE_CONTROL_AT)");
                                    }
                                    else
                                    {
                                        var ix = BitConverter.ToInt32(data, 0);
                                        var iy = BitConverter.ToInt32(data, 4);
                                        Console.WriteLine($"[UDP][RecvFallbackInt] -> ({ix},{iy})");
                                        InputHooks.SuppressNextMoveFrom(ix, iy);
                                        SetCursorPos(ix, iy);
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"[UDP][Receive] Unknown packet type 0x{data[0]:X2}, length {data.Length} from {remoteEP.Address}:{remoteEP.Port}");
                                }
                                break;
                        }
                    }
                    catch (ThreadAbortException)
                    {
                        Console.WriteLine("[UDP][RecvLoop] ThreadAbortException");
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (!_running) break;
                        Console.WriteLine($"[UDP][Receive] Exception: {ex.Message}");
                        Thread.Sleep(1);
                    }
                }
            }
            finally
            {
                Console.WriteLine("[UDP][RecvLoop] Exit");
            }
        }
    }
}