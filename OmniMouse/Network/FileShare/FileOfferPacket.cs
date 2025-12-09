using System;

namespace OmniMouse.Network.FileShare
{
    /// <summary>
    /// Represents a file offer notification sent from sender to receiver via UDP.
    /// Contains metadata needed for the receiver to request the file via TCP.
    /// </summary>
    public sealed class FileOfferPacket
    {
        /// <summary>
        /// Unique identifier for this file transfer session.
        /// </summary>
        public Guid FileId { get; set; }

        /// <summary>
        /// Original filename (without path).
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Total file size in bytes.
        /// </summary>
        public long FileSizeBytes { get; set; }

        /// <summary>
        /// TCP port on which the sender's FileTransferServer is listening.
        /// </summary>
        public int TcpPort { get; set; }

        /// <summary>
        /// Optional: Client ID of the sender machine.
        /// </summary>
        public string? SenderClientId { get; set; }

        /// <summary>
        /// Optional: Timestamp when the file was offered.
        /// </summary>
        public DateTime OfferedAt { get; set; } = DateTime.UtcNow;

        public override string ToString() =>
            $"FileOffer[{FileId:N}]: {FileName} ({FormatFileSize(FileSizeBytes)}) on port {TcpPort}";

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = ["B", "KB", "MB", "GB", "TB"];
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
