using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace OmniMouse.Network.FileShare
{
    /// <summary>
    /// TCP server that streams files to remote clients on demand.
    /// Listens on an OS-assigned port and serves files based on staged FileIDs.
    /// </summary>
    public sealed class FileTransferServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly ConcurrentDictionary<Guid, string> _stagedFiles = new();
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _listenerTask;
        private bool _disposed;

        /// <summary>
        /// Gets the TCP port on which this server is listening.
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileTransferServer"/> class.
        /// Binds to any available port (port 0 = OS-assigned).
        /// </summary>
        public FileTransferServer()
        {
            _listener = new TcpListener(IPAddress.Any, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Console.WriteLine($"[FileTransferServer] Started on port {Port}");
        }

        /// <summary>
        /// Stages a file for transfer and returns a unique FileID.
        /// </summary>
        /// <param name="filePath">Absolute path to the file to stage.</param>
        /// <returns>A unique identifier for this file transfer session.</returns>
        /// <exception cref="FileNotFoundException">If the file does not exist.</exception>
        public Guid StageFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Cannot stage a file that does not exist.", filePath);

            var fileId = Guid.NewGuid();
            _stagedFiles[fileId] = filePath;
            Console.WriteLine($"[FileTransferServer] Staged file: {Path.GetFileName(filePath)} -> {fileId:N}");
            return fileId;
        }

        /// <summary>
        /// Unstages a file (removes it from available transfers).
        /// </summary>
        /// <param name="fileId">The file ID to remove.</param>
        /// <returns>True if the file was removed; false if not found.</returns>
        public bool UnstageFile(Guid fileId)
        {
            var removed = _stagedFiles.TryRemove(fileId, out var path);
            if (removed)
            {
                Console.WriteLine($"[FileTransferServer] Unstaged file: {fileId:N}");
            }
            return removed;
        }

        /// <summary>
        /// Starts the server listening loop.
        /// Accepts incoming TCP connections and serves file data asynchronously.
        /// </summary>
        public void Start()
        {
            if (_listenerTask != null)
            {
                Console.WriteLine("[FileTransferServer] Already started.");
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _listenerTask = Task.Run(() => AcceptClientsAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
            Console.WriteLine("[FileTransferServer] Listener loop started.");
        }

        /// <summary>
        /// Stops the server and cancels all pending operations.
        /// </summary>
        public void Stop()
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _listenerTask?.Wait(TimeSpan.FromSeconds(2));
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }

            _listenerTask = null;
            Console.WriteLine("[FileTransferServer] Stopped.");
        }

        private async Task AcceptClientsAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("[FileTransferServer] Accepting clients...");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FileTransferServer] Accept error: {ex.Message}");
                }
            }

            Console.WriteLine("[FileTransferServer] Accept loop exited.");
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            var clientEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
            Console.WriteLine($"[FileTransferServer] Client connected: {clientEndpoint}");

            try
            {
                await using var stream = client.GetStream();

                // Read FileID (16 bytes)
                var fileIdBytes = new byte[16];
                var bytesRead = await stream.ReadAsync(fileIdBytes, 0, 16, cancellationToken).ConfigureAwait(false);

                if (bytesRead != 16)
                {
                    Console.WriteLine($"[FileTransferServer] Invalid FileID length from {clientEndpoint}");
                    return;
                }

                var fileId = new Guid(fileIdBytes);
                Console.WriteLine($"[FileTransferServer] Request for FileID: {fileId:N} from {clientEndpoint}");

                // Check if file is staged
                if (_stagedFiles.TryGetValue(fileId, out var filePath) && File.Exists(filePath))
                {
                    // Send success status (1 byte: true)
                    await stream.WriteAsync(new byte[] { 1 }, 0, 1, cancellationToken).ConfigureAwait(false);

                    // Stream file data
                    await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, System.IO.FileShare.Read, 81920, useAsync: true);
                    await fileStream.CopyToAsync(stream, 81920, cancellationToken).ConfigureAwait(false);

                    Console.WriteLine($"[FileTransferServer] Sent file: {Path.GetFileName(filePath)} ({fileStream.Length:N0} bytes) to {clientEndpoint}");
                }
                else
                {
                    // Send failure status (1 byte: false)
                    await stream.WriteAsync(new byte[] { 0 }, 0, 1, cancellationToken).ConfigureAwait(false);
                    Console.WriteLine($"[FileTransferServer] File not found for FileID: {fileId:N}");
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[FileTransferServer] Transfer cancelled for {clientEndpoint}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileTransferServer] Error handling client {clientEndpoint}: {ex.Message}");
            }
            finally
            {
                client.Close();
            }
        }

        /// <summary>
        /// Disposes the server and releases all resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            Stop();
            _listener.Stop();
            _stagedFiles.Clear();
            _disposed = true;

            Console.WriteLine("[FileTransferServer] Disposed.");
        }
    }
}
