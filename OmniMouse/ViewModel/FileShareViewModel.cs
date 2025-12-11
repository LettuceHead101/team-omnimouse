using OmniMouse.MVVM;
using OmniMouse.Network;
using OmniMouse.Network.FileShare;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using OmniMouse.Configuration;

namespace OmniMouse.ViewModel
{
    /// <summary>
    /// ViewModel for managing file sharing operations.
    /// Displays incoming file offers and handles download requests.
    /// </summary>
    public class FileShareViewModel : ViewModelBase
    {
        private readonly FileTransferServer _transferServer;
        private string _statusMessage = "No file transfers in progress";
        private bool _isDownloading;

        /// <summary>
        /// Gets the collection of incoming file offers from remote peers.
        /// </summary>
        public ObservableCollection<FileOfferPacket> IncomingFiles { get; } = new();

        /// <summary>
        /// Gets the collection of files staged for sending.
        /// </summary>
        public ObservableCollection<StagedFileInfo> StagedFiles { get; } = new();

        /// <summary>
        /// Gets or sets the current status message.
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// Gets or sets whether a download is currently in progress.
        /// </summary>
        public bool IsDownloading
        {
            get => _isDownloading;
            set => SetProperty(ref _isDownloading, value);
        }

        /// <summary>
        /// Command to download a selected file offer.
        /// </summary>
        public ICommand DownloadFileCommand { get; }

        /// <summary>
        /// Command to remove a staged file.
        /// </summary>
        public ICommand RemoveStagedFileCommand { get; }

        /// <summary>
        /// Gets or sets the default folder where downloads will be saved.
        /// </summary>
        public string DownloadFolderPath
        {
            get => App.Settings?.DefaultDownloadFolder ?? string.Empty;
            set
            {
                if (App.Settings == null)
                    return;
                if (string.Equals(App.Settings.DefaultDownloadFolder, value, StringComparison.Ordinal))
                    return;
                App.Settings.DefaultDownloadFolder = value ?? string.Empty;
                App.Settings.Save();
                OnPropertyChanged(nameof(DownloadFolderPath));
            }
        }

        /// <summary>
        /// Command to browse and update the download folder.
        /// </summary>
        public ICommand BrowseDownloadFolderCommand { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileShareViewModel"/> class.
        /// </summary>
        public FileShareViewModel()
        {
            _transferServer = new FileTransferServer();
            _transferServer.Start();

            DownloadFileCommand = new RelayCommand(
                async param => await ExecuteDownloadFileAsync(param),
                param => param is FileOfferPacket && !IsDownloading);

            RemoveStagedFileCommand = new RelayCommand(
                param => ExecuteRemoveStagedFile(param),
                param => param is StagedFileInfo);

            BrowseDownloadFolderCommand = new RelayCommand(
                _ => ExecuteBrowseDownloadFolder(),
                _ => true);
        }

        /// <summary>
        /// Registers a UDP transmitter to receive file offer notifications.
        /// </summary>
        /// <param name="transmitter">The UDP transmitter instance.</param>
        public void RegisterTransmitter(IUdpMouseTransmitter transmitter)
        {
            if (transmitter == null)
                throw new ArgumentNullException(nameof(transmitter));

            transmitter.FileOfferReceived += OnFileOfferReceived;
        }

        /// <summary>
        /// Unregisters a UDP transmitter.
        /// </summary>
        /// <param name="transmitter">The UDP transmitter instance.</param>
        public void UnregisterTransmitter(IUdpMouseTransmitter transmitter)
        {
            if (transmitter != null)
            {
                transmitter.FileOfferReceived -= OnFileOfferReceived;
            }
        }

        /// <summary>
        /// Stages a file for transfer and returns the offer packet to send to peer.
        /// </summary>
        /// <param name="filePath">Absolute path to the file to stage.</param>
        /// <returns>A FileOfferPacket ready to be sent via UDP.</returns>
        public FileOfferPacket StageFileForTransfer(string filePath)
        {
            var fileId = _transferServer.StageFile(filePath);
            var fileInfo = new System.IO.FileInfo(filePath);

            var offer = new FileOfferPacket
            {
                FileId = fileId,
                FileName = fileInfo.Name,
                FileSizeBytes = fileInfo.Length,
                TcpPort = _transferServer.Port,
                OfferedAt = DateTime.UtcNow
            };

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                StagedFiles.Add(new StagedFileInfo
                {
                    FileId = fileId,
                    FilePath = filePath,
                    FileName = fileInfo.Name,
                    FileSizeBytes = fileInfo.Length,
                    StagedAt = DateTime.UtcNow
                });

                StatusMessage = $"Staged: {fileInfo.Name} ({FormatFileSize(fileInfo.Length)})";
            });

            return offer;
        }

        private void OnFileOfferReceived(FileOfferPacket offer)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                IncomingFiles.Add(offer);
                StatusMessage = $"Received file offer: {offer.FileName}";
                Console.WriteLine($"[FileShareVM] Added incoming file: {offer}");
            });
        }

        private async Task ExecuteDownloadFileAsync(object? parameter)
        {
            if (parameter is not FileOfferPacket offer)
                return;

            IsDownloading = true;
            StatusMessage = $"Downloading {offer.FileName}...";

            try
            {
                // Use the sender's IP from the offer
                var hostIp = offer.SenderClientId ?? "127.0.0.1";

                var progress = new Progress<long>(bytesDownloaded =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var percent = offer.FileSizeBytes > 0
                            ? (bytesDownloaded * 100.0 / offer.FileSizeBytes)
                            : 0;
                        StatusMessage = $"Downloading {offer.FileName}: {percent:F1}% ({FormatFileSize(bytesDownloaded)} / {FormatFileSize(offer.FileSizeBytes)})";
                    });
                });

                var destinationDir = App.Settings?.DefaultDownloadFolder ?? string.Empty;
                var destinationPath = await FileTransferClient.DownloadFileAsync(
                    hostIp,
                    offer.TcpPort,
                    offer.FileId,
                    offer.FileName,
                    destinationDir,
                    progress).ConfigureAwait(false);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"Download complete: {offer.FileName} saved to {destinationPath}";
                    IncomingFiles.Remove(offer);

                    System.Windows.MessageBox.Show(
                        $"File downloaded successfully!\n\nSaved to: {destinationPath}",
                        "Download Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"Download failed: {ex.Message}";
                    System.Windows.MessageBox.Show(
                        $"Failed to download file:\n\n{ex.Message}",
                        "Download Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            }
            finally
            {
                IsDownloading = false;
            }
        }

        private void ExecuteBrowseDownloadFolder()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Choose where incoming files are saved",
                    CheckFileExists = false,
                    CheckPathExists = true,
                    ValidateNames = false,
                    FileName = "Select Folder",
                    InitialDirectory = string.IsNullOrWhiteSpace(DownloadFolderPath) ? null : DownloadFolderPath,
                };

                var result = dialog.ShowDialog();
                if (result == true)
                {
                    var selected = System.IO.Path.GetDirectoryName(dialog.FileName);
                    if (!string.IsNullOrWhiteSpace(selected))
                    {
                        DownloadFolderPath = selected;
                        StatusMessage = $"Save to: {selected}";
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to choose folder:\n\n{ex.Message}",
                    "Folder Selection Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ExecuteRemoveStagedFile(object? parameter)
        {
            if (parameter is not StagedFileInfo staged)
                return;

            _transferServer.UnstageFile(staged.FileId);
            StagedFiles.Remove(staged);
            StatusMessage = $"Removed: {staged.FileName}";
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        /// <summary>
        /// Cleans up resources.
        /// </summary>
        public void Cleanup()
        {
            _transferServer?.Dispose();
        }
    }

    /// <summary>
    /// Represents a file that has been staged for outgoing transfer.
    /// </summary>
    public class StagedFileInfo
    {
        public Guid FileId { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public DateTime StagedAt { get; set; }
    }
}
