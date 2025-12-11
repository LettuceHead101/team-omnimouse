using System;
using System.Linq;
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
                //Console.WriteLine($"[UDP][SendMouse] Delta ({x},{y}) encoded as ({encodedX},{encodedY})");
            }
            else
            {
                //Console.WriteLine($"[UDP][SendMouse] Absolute ({x},{y})");
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
            //Console.WriteLine($"[UDP][SendBtn] {button} {(isDown ? "DOWN" : "UP")}");
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
            //Console.WriteLine($"[UDP][SendWheel] delta={delta}");
        }

        /// <summary>
        /// Sends a file offer notification to the remote peer via UDP.
        /// The peer can then connect back via TCP to download the file.
        /// </summary>
        public void SendFileOffer(FileShare.FileOfferPacket offer)
        {
            if (offer == null)
                throw new ArgumentNullException(nameof(offer));

            lock (_roleLock)
            {
                if (!_handshakeComplete)
                {
                    //Console.WriteLine("[UDP][SendFileOffer] Handshake not complete - cannot send file offer");
                    return;
                }
            }

            if (_udpClient == null || _remoteEndPoint == null)
            {
                //Console.WriteLine("[UDP][SendFileOffer] No UDP client or remote endpoint configured");
                return;
            }

            try
            {
                // Serialize the file offer packet to JSON
                var json = System.Text.Json.JsonSerializer.Serialize(offer);
                var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);

                // Build packet: MSG_FILE_OFFER + 4-byte length + JSON payload
                var buf = new byte[1 + 4 + jsonBytes.Length];
                buf[0] = MSG_FILE_OFFER;
                Array.Copy(BitConverter.GetBytes(jsonBytes.Length), 0, buf, 1, 4);
                Array.Copy(jsonBytes, 0, buf, 5, jsonBytes.Length);

                _udpClient.Send(buf, buf.Length, _remoteEndPoint);
                //Console.WriteLine($"[UDP][SendFileOffer] Sent: {offer}");
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"[UDP][SendFileOffer] Error: {ex.Message}");
            }
        }

        public void SendTakeControl(string targetClientId, int localX, int localY)
        {
            // Delegate to the overload with no direction (backward compat)
            SendTakeControl(targetClientId, localX, localY, entryDirection: null);
        }

        /// <summary>
        /// Sends a take-control message to the target client, optionally including the entry direction.
        /// The entry direction tells the Receiver which edge to check for returning control.
        /// </summary>
        public void SendTakeControl(string targetClientId, int localX, int localY, OmniMouse.Switching.Direction? entryDirection)
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
                    //Console.WriteLine($"[TCP][TakeControl] Cannot send: unknown endpoint for {targetClientId} and no learned remote endpoint.");
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
                            //Console.WriteLine($"[TCP][TakeControl] Reusing existing connection to {targetClientId}");
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
                    //Console.WriteLine($"[TCP][TakeControl] Establishing new connection to {targetClientId} ({tcpEndPoint.Address}:{tcpEndPoint.Port})");
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
                                //Console.WriteLine($"[TCP][TakeControl] Connect attempt {attempt} failed ({ex.Message}), retrying...");
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
                // Format: [MSG_TAKE_CONTROL_AT][localX: 4 bytes][localY: 4 bytes][direction: 1 byte]
                // Direction: 0xFF = none, 0x00 = Left, 0x01 = Right, 0x02 = Up, 0x03 = Down
                var buf = new byte[1 + 4 + 4 + 1];
                buf[0] = MSG_TAKE_CONTROL_AT;
                BitConverter.TryWriteBytes(buf.AsSpan(1, 4), localX);
                BitConverter.TryWriteBytes(buf.AsSpan(5, 4), localY);
                buf[9] = entryDirection.HasValue ? (byte)entryDirection.Value : (byte)0xFF;

                // Send the packet
                stream.WriteTimeout = 5000;
                stream.Write(buf, 0, buf.Length);
                stream.Flush();
                //Console.WriteLine($"[TCP][TakeControl] Sent take-control to {targetClientId} ({tcpEndPoint.Address}) at ({localX},{localY}) dir={entryDirection}");

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

                //Console.WriteLine($"[TCP][TakeControl] Received acknowledgment from {targetClientId}");

                // We are now the active Sender that forwards local input to the target.
                // Keep hooks active and enable send-path by setting local role to Sender.
                this.SetLocalRole(ConnectionRole.Sender);
            }
            catch (SocketException ex)
            {
                //Console.WriteLine($"[TCP][TakeControl] Socket error: {ex.Message}");
                
                // Clean up failed connection
                lock (_tcpLock)
                {
                    _tcpConnections.Remove(targetClientId);
                }
                
                try { tcpClient?.Close(); } catch { }
                //Console.WriteLine("[TCP][TakeControl] Hints: ensure peer is running, TCP 5001 is listening, and firewall allows inbound 5001. Try: Test-NetConnection -ComputerName {0} -Port {1}", tcpEndPoint.Address, tcpEndPoint.Port);
                throw new InvalidOperationException($"Failed to establish TCP connection to {targetClientId} ({tcpEndPoint.Address}:{tcpEndPoint.Port}): {ex.Message}", ex);
            }
            catch (IOException ex)
            {
                //Console.WriteLine($"[TCP][TakeControl] IO error: {ex.Message}");
                
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
                //Console.WriteLine($"[TCP][TakeControl] Unexpected error: {ex.Message}");
                
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
            //Console.WriteLine("[UDP][RecvLoop] Enter");
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
                            //Console.WriteLine($"[UDP][RecvLoop] alive. iterations={iters}, lastAlive={_recvLoopLastAlive:O}");
                            lastHeartbeat = DateTime.UtcNow;
                        }

                        if (data == null || data.Length == 0) continue;

                        if (_remoteEndPoint == null)
                        {
                            _remoteEndPoint = remoteEP;
                            //Console.WriteLine($"[UDP][RecvLoop] Learned remote endpoint: {_remoteEndPoint}");
                        }

                        // Message dispatch
                        byte msgType = data[0];

                        switch (msgType)
                        {
                            case 0x10: // Handshake request
                                HandleHandshakeRequest(data, remoteEP);
                                break;
                            case 0x11: // Handshake accept
                                HandleHandshakeAccept(data, remoteEP);
                                break;
                            case 0x12: // MSG_ROLE_SWITCH_REQUEST
                                HandleRoleSwitchRequest(remoteEP);
                                break;
                            case 0x13: // MSG_ROLE_SWITCH_ACCEPT
                                HandleRoleSwitchAccept(remoteEP);
                                break;
                            case 0x14: // MSG_DISCONNECT - Peer is disconnecting gracefully
                                HandlePeerDisconnect(remoteEP);
                                break;
                            case 0x20: // PRE-FLIGHT REQUEST
                                HandlePreFlightRequest(remoteEP);
                                break;
                            case 0x21: // PRE-FLIGHT ACK - Response from Receiver
                                InputHooks.OnPreFlightAckReceived();
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

                                        //Console.WriteLine($"[UDP][RecvMouse] Relative delta ({deltaX},{deltaY}) from encoded ({x},{y})");
                                        
                                        // STEP 1: Capture cursor position BEFORE injection for comparison
                                        int beforeX = 0, beforeY = 0;
                                        bool hadBefore = GetCursorPos(out var beforePos);
                                        if (hadBefore)
                                        {
                                            beforeX = beforePos.x;
                                            beforeY = beforePos.y;
                                        }
                                        
                                        // STEP 2: Inject the delta - this moves the physical cursor
                                        // SendInput is synchronous for cursor position - it updates before returning
                                        InputHooks.InjectMouseDelta(deltaX, deltaY);
                                        
                                        // STEP 3: Query the ACTUAL cursor position from Windows IMMEDIATELY
                                        // No sleep needed - SendInput updates cursor position synchronously
                                        // This is the AUTHORITATIVE source - not accumulated deltas
                                        if (GetCursorPos(out var actualPos))
                                        {
                                            _receiverLocalCursorX = actualPos.x;
                                            _receiverLocalCursorY = actualPos.y;
                                            

                                            // Log the actual vs expected for debugging drift
                                            int expectedX = beforeX + deltaX;
                                            int expectedY = beforeY + deltaY;
                                            if (Math.Abs(_receiverLocalCursorX - expectedX) > 2 || 
                                                Math.Abs(_receiverLocalCursorY - expectedY) > 2)
                                            {
                                                //Console.WriteLine($"[UDP][RecvMouse][DRIFT] Expected ({expectedX},{expectedY}) but actual is ({_receiverLocalCursorX},{_receiverLocalCursorY})");
                                            }
                                            

                                            // STEP 5: RECEIVER EDGE DETECTION using ACTUAL OS position
                                            CheckReceiverLocalEdgeHit(this, _receiverLocalCursorX, _receiverLocalCursorY);
                                        }
                                        else
                                        {
                                            //Console.WriteLine("[UDP][RecvMouse] WARNING: GetCursorPos failed - edge detection may be inaccurate");
                                        }
                                    }
                                    else
                                    {
                                        // Absolute position
                                        //Console.WriteLine($"[UDP][RecvMouse] Absolute ({x},{y})");
                                        InputHooks.SuppressNextMoveFrom(x, y);
                                        SetCursorPos(x, y);
                                        
                                        // For absolute moves, query actual position immediately
                                        // SetCursorPos is synchronous - cursor position is updated before it returns
                                        if (GetCursorPos(out var actualPos))
                                        {
                                            _receiverLocalCursorX = actualPos.x;
                                            _receiverLocalCursorY = actualPos.y;
                                        }
                                        else
                                        {
                                            // Fallback: use the intended position
                                            _receiverLocalCursorX = x;
                                            _receiverLocalCursorY = y;
                                        }
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
                                    //Console.WriteLine($"[UDP][RecvBtn] {button} {(isDown ? "DOWN" : "UP")}");
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
                                    //Console.WriteLine($"[UDP][RecvWheel] delta={delta}");
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
                                    //Console.WriteLine($"[UDP][RecvLegacy] from {remoteEP.Address}:{remoteEP.Port} -> ({x},{y})");
                                    InputHooks.SuppressNextMoveFrom(x, y);
                                    SetCursorPos(x, y);
                                }
                                break;

                            case MSG_TAKE_CONTROL_AT:
                                if (_remoteEndPoint != null && !remoteEP.Address.Equals(_remoteEndPoint.Address))
                                {
                                    //Console.WriteLine($"[UDP][TakeControl][DROP] from unexpected {remoteEP.Address}");
                                    break;
                                }

                                if (data.Length >= 1 + 8)
                                {
                                    var ux = BitConverter.ToInt32(data, 1);
                                    var uy = BitConverter.ToInt32(data, 1 + 4);
                                    //Console.WriteLine($"[UDP][TakeControl] Received UNIVERSAL ({ux},{uy}) from {remoteEP.Address}:{remoteEP.Port}");

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
                                        try { TakeControlReceived?.Invoke(pixel.X, pixel.Y); } catch (Exception ex) {
                                            //Console.WriteLine($"[UDP][TakeControl] handler error: {ex.Message}");
                                        }
                                    }
                                    catch (Exception mapEx)
                                    {
                                        //Console.WriteLine($"[UDP][TakeControl] Mapping failed: {mapEx.Message}");
                                    }
                                }

                                // When receiver takes control (cursor placed at specific position)
                                // Reset tracking so it re-initializes from the new position
                                ResetReceiverCursorTracking();
                                break;

                            case MSG_LAYOUT_UPDATE:
                                if (_remoteEndPoint != null && !remoteEP.Address.Equals(_remoteEndPoint.Address))
                                {
                                    //Console.WriteLine($"[UDP][LayoutUpdate][DROP] from unexpected {remoteEP.Address}");
                                    break;
                                }

                                if (data.Length > 1)
                                {
                                    HandleLayoutUpdate(data, 1);
                                }
                                break;

                            case MSG_GRID_LAYOUT_UPDATE:
                                if (_remoteEndPoint != null && !remoteEP.Address.Equals(_remoteEndPoint.Address))
                                {
                                    //Console.WriteLine($"[UDP][GridLayoutUpdate][DROP] from unexpected {remoteEP.Address}");
                                    break;
                                }

                                if (data.Length > 1)
                                {
                                    HandleGridLayoutUpdate(data, 1);
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
                                    //Console.WriteLine($"[UDP][MonitorInfo][DROP] from unexpected {remoteEP.Address}");
                                    break;
                                }

                                if (data.Length > 1)
                                {
                                    HandleMonitorInfo(data, 1);
                                }
                                break;

                            case MSG_FILE_OFFER:
                                if (_remoteEndPoint != null && !remoteEP.Address.Equals(_remoteEndPoint.Address))
                                {
                                    //Console.WriteLine($"[UDP][FileOffer][DROP] from unexpected {remoteEP.Address}");
                                    break;
                                }

                                if (data.Length > 5)
                                {
                                    try
                                    {
                                        var jsonLength = BitConverter.ToInt32(data, 1);
                                        if (data.Length >= 5 + jsonLength)
                                        {
                                            var jsonBytes = new byte[jsonLength];
                                            Array.Copy(data, 5, jsonBytes, 0, jsonLength);
                                            var json = System.Text.Encoding.UTF8.GetString(jsonBytes);
                                            var offer = System.Text.Json.JsonSerializer.Deserialize<FileShare.FileOfferPacket>(json);

                                            if (offer != null)
                                            {
                                                // Update the sender IP from the remote endpoint
                                                offer.SenderClientId = remoteEP.Address.ToString();
                                                //Console.WriteLine($"[UDP][FileOffer] Received: {offer}");
                                                
                                                try
                                                {
                                                    FileOfferReceived?.Invoke(offer);
                                                }
                                                catch (Exception handlerEx)
                                                {
                                                    //Console.WriteLine($"[UDP][FileOffer] Handler error: {handlerEx.Message}");
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        //Console.WriteLine($"[UDP][FileOffer] Parse error: {ex.Message}");
                                    }
                                }
                                break;

                            case MSG_RECEIVER_EDGE_HIT:
                                HandleReceiverEdgeHit(remoteEP);
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
                                        //Console.WriteLine($"[UDP][RecvFallbackFloat] nx={nx:F6}, ny={ny:F6} (legacy - bitmap approach uses MSG_TAKE_CONTROL_AT)");
                                    }
                                    else
                                    {
                                        var ix = BitConverter.ToInt32(data, 0);
                                        var iy = BitConverter.ToInt32(data, 4);
                                        //Console.WriteLine($"[UDP][RecvFallbackInt] -> ({ix},{iy})");
                                        InputHooks.SuppressNextMoveFrom(ix, iy);
                                        SetCursorPos(ix, iy);
                                    }
                                }
                                else
                                {
                                    //Console.WriteLine($"[UDP][Receive] Unknown packet type 0x{data[0]:X2}, length {data.Length} from {remoteEP.Address}:{remoteEP.Port}");
                                }
                                break;
                        }
                    }
                    catch (ThreadAbortException)
                    {
                        //Console.WriteLine("[UDP][RecvLoop] ThreadAbortException");
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (!_running) break;
                        //Console.WriteLine($"[UDP][Receive] Exception: {ex.Message}");
                        Thread.Sleep(1);
                    }
                }
            }
            finally
            {
                //Console.WriteLine("[UDP][RecvLoop] Exit");
            }
        }

        // Handle pre-flight verification request
        private void HandlePreFlightRequest(IPEndPoint remoteEP)
        {
            //Console.WriteLine($"[UDP][PreFlight] Received preflight request from {remoteEP}");

            try
            {
                // Receiver: Always respond immediately with ACK
                // NO checks on local state - just confirm we're reachable
                var buf = new byte[1];
                buf[0] = 0x21; // MSG_PREFLIGHT_ACK
                _udpClient?.Send(buf, buf.Length, remoteEP);
                //Console.WriteLine($"[UDP][PreFlight] Sent ACK to {remoteEP}");
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"[UDP][PreFlight] Failed to send ACK: {ex.Message}");
            }
        }

        // Handle peer disconnect notification
        private void HandlePeerDisconnect(IPEndPoint remoteEP)
        {
            //Console.WriteLine($"[UDP][Disconnect] Peer {remoteEP} is disconnecting gracefully");
            
            // Only process if this is from our known peer
            if (_remoteEndPoint != null && !remoteEP.Address.Equals(_remoteEndPoint.Address))
            {
                //Console.WriteLine($"[UDP][Disconnect] Ignoring disconnect from unknown peer {remoteEP}");
                return;
            }
            
            // Fully reset all InputHooks peer connection state
            // This handles both Sender and Receiver scenarios
            InputHooks.ResetPeerConnectionState();
            
            // Reset handshake state so we can reconnect
            lock (_roleLock)
            {
                _handshakeComplete = false;
                _currentRole = ConnectionRole.Receiver; // Default back to receiver
            }
            
            // Clear the remote endpoint
            _remoteEndPoint = null;
            
            // Reset receiver tracking state
            ResetReceiverCursorTracking();
            
            // Reset layout coordinator so it can be re-initialized on reconnect
            ResetLayoutCoordinator();
            
            // Clear any cached TCP connections to this peer
            lock (_tcpLock)
            {
                foreach (var kv in _tcpConnections.ToList())
                {
                    try { kv.Value.Close(); } catch { }
                    _tcpConnections.Remove(kv.Key);
                }
            }
            
            //Console.WriteLine("[UDP][Disconnect] State reset, ready for new connection");
            
            // Notify UI/listeners about disconnect and role change
            try { PeerDisconnected?.Invoke(); } catch { }
            try { RoleChanged?.Invoke(ConnectionRole.Receiver); } catch { }
        }

        // NEW handler
        private void HandleReceiverEdgeHit(IPEndPoint remoteEP)
        {
            //Console.WriteLine($"[UDP][EdgeHit] Receiver reported edge hit from {remoteEP}");
            
            lock (_roleLock)
            {
                if (!_handshakeComplete || _currentRole != ConnectionRole.Sender)
                {
                    //Console.WriteLine("[UDP][EdgeHit] Not Sender, ignoring");
                    return;
                }
            }
            
            // Signal InputHooks to run TryEdgeReturn on Sender side
            InputHooks.NotifyReceiverEdgeHit();  // NEW
        }

        // Edge threshold for Receiver local edge detection
        private const int ReceiverEdgeThresholdPixels = 5;
        
        // Debounce for Receiver edge notifications (prevent spamming)
        private DateTime _lastReceiverEdgeNotification = DateTime.MinValue;
        private const int ReceiverEdgeNotificationDebounceMs = 100;

        // Track Receiver's cursor position (queried from OS after each injection)
        private int _receiverLocalCursorX = 0;
        private int _receiverLocalCursorY = 0;
        
        // Track which direction control entered from (set when Receiver gets MSG_TAKE_CONTROL_AT)
        // This determines which edge the Receiver needs to hit to return control
        private OmniMouse.Switching.Direction? _receiverEntryDirection = null;

        /// <summary>
        /// Called on Receiver after injecting mouse deltas.
        /// Checks if the LOCAL cursor hit an edge and notifies the Sender.
        /// The Sender will verify mutual agreement before ending the stream.
        /// </summary>
        /// <param name="tx">The transmitter instance</param>
        /// <param name="trackedX">The tracked local cursor X position (queried from OS after injection)</param>
        /// <param name="trackedY">The tracked local cursor Y position (queried from OS after injection)</param>
        private static void CheckReceiverLocalEdgeHit(UdpMouseTransmitter tx, int trackedX, int trackedY)
        {
            // CRITICAL: trackedX/trackedY should be from GetCursorPos, NOT accumulated deltas
            // This ensures we use the actual OS cursor position for edge detection
            
            int localX = trackedX;
            int localY = trackedY;

            // Get Receiver's LOCAL virtual screen bounds
            // IMPORTANT: VirtualScreen.Right and .Bottom are EXCLUSIVE bounds (one past last pixel)
            int left = System.Windows.Forms.SystemInformation.VirtualScreen.Left;
            int top = System.Windows.Forms.SystemInformation.VirtualScreen.Top;
            int right = System.Windows.Forms.SystemInformation.VirtualScreen.Right;
            int bottom = System.Windows.Forms.SystemInformation.VirtualScreen.Bottom;

            // Convert to INCLUSIVE bounds (actual pixel coordinates of edges)
            int rightInclusive = right - 1;   // Rightmost pixel X coordinate
            int bottomInclusive = bottom - 1; // Bottommost pixel Y coordinate

            var now = DateTime.UtcNow;
            
            // Determine which edge the Receiver needs to hit to RETURN control to Sender
            // This is the OPPOSITE of the entry direction
            // e.g., if Sender entered via RIGHT edge, Receiver must hit LEFT edge to return
            bool atReturnEdge = false;
            string returnEdgeDesc = "NONE";
            
            if (tx._receiverEntryDirection.HasValue)
            {
                switch (tx._receiverEntryDirection.Value)
                {
                    case OmniMouse.Switching.Direction.Right:
                        // Sender crossed their RIGHT edge to get here
                        // Receiver must hit LEFT edge to return
                        atReturnEdge = localX <= left + ReceiverEdgeThresholdPixels;
                        returnEdgeDesc = "LEFT";
                        break;
                    case OmniMouse.Switching.Direction.Left:
                        // Sender crossed their LEFT edge to get here  
                        // Receiver must hit RIGHT edge to return
                        atReturnEdge = localX >= rightInclusive - ReceiverEdgeThresholdPixels;
                        returnEdgeDesc = "RIGHT";
                        break;
                    case OmniMouse.Switching.Direction.Down:
                        // Sender crossed their BOTTOM edge to get here
                        // Receiver must hit TOP edge to return
                        atReturnEdge = localY <= top + ReceiverEdgeThresholdPixels;
                        returnEdgeDesc = "TOP";
                        break;
                    case OmniMouse.Switching.Direction.Up:
                        // Sender crossed their TOP edge to get here
                        // Receiver must hit BOTTOM edge to return
                        atReturnEdge = localY >= bottomInclusive - ReceiverEdgeThresholdPixels;
                        returnEdgeDesc = "BOTTOM";
                        break;
                }
            }
            else
            {
                // No entry direction known - check all edges as fallback
                bool atLeftEdge = localX <= left + ReceiverEdgeThresholdPixels;
                bool atRightEdge = localX >= rightInclusive - ReceiverEdgeThresholdPixels;
                bool atTopEdge = localY <= top + ReceiverEdgeThresholdPixels;
                bool atBottomEdge = localY >= bottomInclusive - ReceiverEdgeThresholdPixels;
                atReturnEdge = atLeftEdge || atRightEdge || atTopEdge || atBottomEdge;
                returnEdgeDesc = atLeftEdge ? "LEFT" : atRightEdge ? "RIGHT" : atTopEdge ? "TOP" : "BOTTOM";
                //Console.WriteLine($"[UDP][RecvEdge][WARN] No entry direction known - checking all edges");
            }
            
            // DEBUG: Log cursor position and proximity to return edge
            bool nearReturnEdge = false;
            if (tx._receiverEntryDirection.HasValue)
            {
                switch (tx._receiverEntryDirection.Value)
                {
                    case OmniMouse.Switching.Direction.Right:
                        nearReturnEdge = localX <= left + 50;
                        break;
                    case OmniMouse.Switching.Direction.Left:
                        nearReturnEdge = localX >= rightInclusive - 50;
                        break;
                    case OmniMouse.Switching.Direction.Down:
                        nearReturnEdge = localY <= top + 50;
                        break;
                    case OmniMouse.Switching.Direction.Up:
                        nearReturnEdge = localY >= bottomInclusive - 50;
                        break;
                }
            }
            
            if (nearReturnEdge || (now - tx._lastReceiverEdgeNotification).TotalMilliseconds >= 500)
            {
                //Console.WriteLine($"[UDP][RecvEdge][DEBUG] OS_Cursor=({localX},{localY}), VirtualScreen=({left},{top})-({rightInclusive},{bottomInclusive}), EntryDir={tx._receiverEntryDirection}, ReturnEdge={returnEdgeDesc}, NearReturn={nearReturnEdge}");
            }

            if (!atReturnEdge)
            {
                // Not at return edge - nothing to report
                return;
            }

            // Debounce: don't spam notifications
            if ((now - tx._lastReceiverEdgeNotification).TotalMilliseconds < ReceiverEdgeNotificationDebounceMs)
            {
                return;
            }

            tx._lastReceiverEdgeNotification = now;

            //Console.WriteLine($"[UDP][RecvEdge] *** EDGE HIT *** OS_Cursor=({localX},{localY}) at {returnEdgeDesc} edge (return edge for entry={tx._receiverEntryDirection}). Bounds=({left},{top})-({rightInclusive},{bottomInclusive}). Notifying Sender...");

            // Send edge hit notification to Sender
            try
            {
                var buf = new byte[1];
                buf[0] = MSG_RECEIVER_EDGE_HIT;
                tx._udpClient?.Send(buf, buf.Length, tx._remoteEndPoint);
                //Console.WriteLine($"[UDP][RecvEdge] Sent edge hit notification to Sender at {tx._remoteEndPoint}");
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"[UDP][RecvEdge] Failed to send edge notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets the receiver cursor tracking state (call when control transfers away)
        /// </summary>
        internal void ResetReceiverCursorTracking()
        {
            _receiverLocalCursorX = 0;
            _receiverLocalCursorY = 0;
            //Console.WriteLine("[UDP] Receiver cursor tracking reset");
        }
    }
}