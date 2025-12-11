using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace OmniMouse.Network.FileShare
{
    /// <summary>
    /// TCP client that downloads files from a remote FileTransferServer.
    /// </summary>
    public static class FileTransferClient
    {
        /// <summary>
        /// Downloads a file from the remote host asynchronously.
        /// </summary>
        /// <param name="hostIp">IP address of the remote host.</param>
        /// <param name="hostPort">TCP port of the remote FileTransferServer.</param>
        /// <param name="fileId">Unique identifier for the file to download.</param>
        /// <param name="fileName">Filename to save as (without path).</param>
        /// <param name="progress">Optional progress callback (bytes downloaded).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Full path to the downloaded file.</returns>
        /// <exception cref="InvalidOperationException">If the server rejects the file request.</exception>
        public static async Task<string> DownloadFileAsync(
            string hostIp,
            int hostPort,
            Guid fileId,
            string fileName,
            IProgress<long>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(hostIp))
                throw new ArgumentException("Host IP cannot be null or empty.", nameof(hostIp));

            if (hostPort <= 0 || hostPort > 65535)
                throw new ArgumentOutOfRangeException(nameof(hostPort), "Port must be between 1 and 65535.");

            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("Filename cannot be null or empty.", nameof(fileName));

            // Create Downloads directory in the application directory
            var downloadDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads");
            Directory.CreateDirectory(downloadDir);

            // Sanitize filename to prevent path traversal attacks
            var safeFileName = Path.GetFileName(fileName);
            var destinationPath = Path.Combine(downloadDir, safeFileName);

            // Handle duplicate filenames
            destinationPath = GetUniqueFilePath(destinationPath);

            //Console.WriteLine($"[FileTransferClient] Connecting to {hostIp}:{hostPort} for FileID: {fileId:N}");

            using var client = new TcpClient();
            await client.ConnectAsync(hostIp, hostPort, cancellationToken).ConfigureAwait(false);

            await using var stream = client.GetStream();

            // Send FileID (16 bytes)
            var fileIdBytes = fileId.ToByteArray();
            await stream.WriteAsync(fileIdBytes, 0, 16, cancellationToken).ConfigureAwait(false);

            //Console.WriteLine($"[FileTransferClient] Sent FileID: {fileId:N}");

            // Read status (1 byte)
            var statusBuffer = new byte[1];
            var bytesRead = await stream.ReadAsync(statusBuffer, 0, 1, cancellationToken).ConfigureAwait(false);

            if (bytesRead != 1)
                throw new InvalidOperationException("Failed to read status from server.");

            var success = statusBuffer[0] == 1;

            if (!success)
                throw new InvalidOperationException($"Server rejected file request for FileID: {fileId:N}");

            //Console.WriteLine($"[FileTransferClient] Server accepted request. Downloading to: {destinationPath}");

            // Stream file data to disk
            await using var fileStream = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                System.IO.FileShare.None,
                81920,
                useAsync: true);

            var buffer = new byte[81920]; // 80 KB buffer
            long totalBytesRead = 0;

            while (true)
            {
                var count = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (count == 0)
                    break;

                await fileStream.WriteAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
                totalBytesRead += count;

                progress?.Report(totalBytesRead);
            }

            //Console.WriteLine($"[FileTransferClient] Download complete: {safeFileName} ({totalBytesRead:N0} bytes)");

            return destinationPath;
        }

        /// <summary>
        /// Downloads a file from the remote host to a specific directory.
        /// </summary>
        /// <param name="hostIp">IP address of the remote host.</param>
        /// <param name="hostPort">TCP port of the remote FileTransferServer.</param>
        /// <param name="fileId">Unique identifier for the file to download.</param>
        /// <param name="fileName">Filename to save as (without path).</param>
        /// <param name="downloadDirectory">Destination folder to save the file.</param>
        /// <param name="progress">Optional progress callback (bytes downloaded).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Full path to the downloaded file.</returns>
        public static async Task<string> DownloadFileAsync(
            string hostIp,
            int hostPort,
            Guid fileId,
            string fileName,
            string downloadDirectory,
            IProgress<long>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(downloadDirectory))
            {
                return await DownloadFileAsync(hostIp, hostPort, fileId, fileName, progress, cancellationToken).ConfigureAwait(false);
            }

            Directory.CreateDirectory(downloadDirectory);

            // Sanitize filename to prevent path traversal attacks
            var safeFileName = Path.GetFileName(fileName);
            var destinationPath = Path.Combine(downloadDirectory, safeFileName);

            // Handle duplicate filenames
            destinationPath = GetUniqueFilePath(destinationPath);

            //Console.WriteLine($"[FileTransferClient] Connecting to {hostIp}:{hostPort} for FileID: {fileId:N}");

            using var client = new TcpClient();
            await client.ConnectAsync(hostIp, hostPort, cancellationToken).ConfigureAwait(false);

            await using var stream = client.GetStream();

            // Send FileID (16 bytes)
            var fileIdBytes = fileId.ToByteArray();
            await stream.WriteAsync(fileIdBytes, 0, 16, cancellationToken).ConfigureAwait(false);

            //Console.WriteLine($"[FileTransferClient] Sent FileID: {fileId:N}");

            // Read status (1 byte)
            var statusBuffer = new byte[1];
            var bytesRead = await stream.ReadAsync(statusBuffer, 0, 1, cancellationToken).ConfigureAwait(false);

            if (bytesRead != 1)
                throw new InvalidOperationException("Failed to read status from server.");

            var success = statusBuffer[0] == 1;

            if (!success)
                throw new InvalidOperationException($"Server rejected file request for FileID: {fileId:N}");

            //Console.WriteLine($"[FileTransferClient] Server accepted request. Downloading to: {destinationPath}");

            // Stream file data to disk
            await using var fileStream = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                System.IO.FileShare.None,
                81920,
                useAsync: true);

            var buffer = new byte[81920]; // 80 KB buffer
            long totalBytesRead = 0;

            while (true)
            {
                var count = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (count == 0)
                    break;

                await fileStream.WriteAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
                totalBytesRead += count;

                progress?.Report(totalBytesRead);
            }

            //Console.WriteLine($"[FileTransferClient] Download complete: {safeFileName} ({totalBytesRead:N0} bytes)");

            return destinationPath;
        }

        private static string GetUniqueFilePath(string filePath)
        {
            if (!File.Exists(filePath))
                return filePath;

            var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);

            var counter = 1;
            string newPath;

            do
            {
                newPath = Path.Combine(directory, $"{fileNameWithoutExtension} ({counter}){extension}");
                counter++;
            }
            while (File.Exists(newPath));

            return newPath;
        }
    }
}
