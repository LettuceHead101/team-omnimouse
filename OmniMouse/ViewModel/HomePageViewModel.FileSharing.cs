namespace OmniMouse.ViewModel
{
    using System;
    using System.Windows.Input;
    using Microsoft.Win32;
    using OmniMouse.MVVM;
    using OmniMouse.Network;
    using OmniMouse.Network.FileShare;
    /// <summary>
    /// Extension methods and integration helpers for adding file sharing to HomePageViewModel.
    /// 
    /// USAGE:
    /// ------
    /// 1. Add this field to HomePageViewModel:
    ///    private FileShareViewModel? _fileShareViewModel;
    /// 
    /// 2. Call InitializeFileSharing() after UDP is started:
    ///    private void ExecuteStartAsSource(object? parameter)
    ///    {
    ///        _udp = new UdpMouseTransmitter();
    ///        _udp.StartHost();
    ///        InitializeFileSharing(); // <-- Add this
    ///    }
    /// 
    /// 3. Add drag-drop support to MainWindow.xaml:
    ///    AllowDrop="True" Drop="Window_Drop"
    /// 
    /// 4. In MainWindow.xaml.cs:
    ///    private void Window_Drop(object sender, DragEventArgs e)
    ///    {
    ///        var vm = (this.DataContext as dynamic)?.HomePageViewModel;
    ///        vm?.HandleFileDrop(e);
    ///    }
    /// </summary>
    public partial class HomePageViewModel
    {
        public ICommand? UploadFileCommand { get; private set; }

        // Add this field to your existing HomePageViewModel
        // private FileShareViewModel? _fileShareViewModel;

        /// <summary>
        /// Initializes file sharing support. Call this after _udp is initialized.
        /// </summary>
        private void InitializeFileSharing()
        {
            if (this._udp == null)
            {
                this.WriteToConsole("[FileShare] Cannot initialize - UDP not ready");
                return;
            }

            try
            {
                this.FileShareViewModel = new FileShareViewModel();
                this.FileShareViewModel.RegisterTransmitter(this._udp);
                this.WriteToConsole("[FileShare] Initialized successfully");

                // Wire the upload command if not already
                this.UploadFileCommand ??= new RelayCommand(_ => this.ExecuteUploadFile());
            }
            catch (Exception ex)
            {
                this.WriteToConsole($"[FileShare] Initialization failed: {ex.Message}");
            }
        }

        private void ExecuteUploadFile()
        {
            if (this._udp == null || !this._udp.HandshakeComplete)
            {
                this.WriteToConsole("[FileShare] Error: Not connected to peer. Establish connection first.");
                return;
            }

            if (this.FileShareViewModel == null)
            {
                this.WriteToConsole("[FileShare] Error: File sharing not initialized");
                return;
            }

            var dlg = new OpenFileDialog
            {
                Title = "Select files to share",
                CheckFileExists = true,
                Multiselect = true,
            };

            var result = dlg.ShowDialog();
            if (result != true)
            {
                return;
            }

            foreach (var filePath in dlg.FileNames)
            {
                try
                {
                    var offer = this.FileShareViewModel.StageFileForTransfer(filePath);
                    this._udp.SendFileOffer(offer);
                    this.WriteToConsole($"[FileShare] Sent offer: {offer.FileName} ({FormatBytes(offer.FileSizeBytes)}) on port {offer.TcpPort}");
                }
                catch (Exception ex)
                {
                    this.WriteToConsole($"[FileShare] Failed to stage file '{filePath}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Handles file drop events from MainWindow.
        /// </summary>
        /// <param name="e">DragEventArgs from Drop event</param>
        public void HandleFileDrop(System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                return;
            }

            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);

            if (files == null || files.Length == 0)
            {
                return;
            }

            // Handle only the first file
            var filePath = files[0];

            // Check if it's a file (not a directory)
            if (!System.IO.File.Exists(filePath))
            {
                WriteToConsole("[FileShare] Error: Please drop a file, not a folder");
                return;
            }

            // Check if connected
            if (_udp == null || !_udp.HandshakeComplete)
            {
                WriteToConsole("[FileShare] Error: Not connected to peer. Establish connection first.");
                return;
            }

            // Check if file sharing is initialized
            if (FileShareViewModel == null)
            {
                WriteToConsole("[FileShare] Error: File sharing not initialized");
                return;
            }

            try
            {
                // Stage the file
                var offer = FileShareViewModel.StageFileForTransfer(filePath);

                // Send the offer to peer
                _udp.SendFileOffer(offer);

                WriteToConsole($"[FileShare] Sent offer: {offer.FileName} ({FormatBytes(offer.FileSizeBytes)}) on port {offer.TcpPort}");
            }
            catch (Exception ex)
            {
                WriteToConsole($"[FileShare] Failed to stage file: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows a dedicated file sharing window.
        /// </summary>
        // TODO: Uncomment when you create FileShareWindow.xaml
        //public void ShowFileShareWindow()
        //{
        //    if (_fileShareViewModel == null)
        //    {
        //        WriteToConsole("[FileShare] Not initialized");
        //        return;
        //    }
        //
        //    try
        //    {
        //        var window = new Views.FileShareWindow(_fileShareViewModel);
        //        window.Show();
        //    }
        //    catch (Exception ex)
        //    {
        //        WriteToConsole($"[FileShare] Failed to open window: {ex.Message}");
        //    }
        //}

        /// <summary>
        /// Cleanup file sharing resources. Call this in your existing Cleanup() method.
        /// </summary>
        private void CleanupFileSharing()
        {
            if (FileShareViewModel != null && _udp != null)
            {
                FileShareViewModel.UnregisterTransmitter(_udp);
            }

            FileShareViewModel?.Cleanup();
            FileShareViewModel = null;
        }

        private static string FormatBytes(long bytes)
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
    }

    /*
     * COMPLETE INTEGRATION EXAMPLE
     * ============================
     * 
     * Add these changes to your existing HomePageViewModel.cs:
     */

    /*
    // 1. Add this field at the top of HomePageViewModel class:
    private FileShareViewModel? _fileShareViewModel;

    // 2. Modify ExecuteStartAsSource:
    private void ExecuteStartAsSource(object? parameter)
    {
        _udp = new UdpMouseTransmitter();
        WireRoleChanged(_udp);
        IsConnected = true;
        IsPeerConfirmed = false;
        IsMouseSource = false;
        WriteToConsole("[UI] Host started. Waiting for peer/handshake...");
        _udp.StartHost();
        _udp.LogDiagnostics();
        
        // Add file sharing support
        InitializeFileSharing();
    }

    // 3. Modify ExecuteStartAsReceiver:
    private void ExecuteStartAsReceiver(object? parameter)
    {
        _udp = new UdpMouseTransmitter();
        WireRoleChanged(_udp);
        IsConnected = true;
        IsPeerConfirmed = false;
        IsMouseSource = false;
        WriteToConsole($"[UI] CoHost started. Attempting handshake to '{RemoteHostIps}'...");
        _udp.StartCoHost(RemoteHostIps);
        _udp.LogDiagnostics();
        
        // Add file sharing support
        InitializeFileSharing();
    }

    // 4. Modify Cleanup method:
    public void Cleanup()
    {
        WriteToConsole("Cleanup called...");
        if (_udp != null)
            _udp.RoleChanged -= OnUdpRoleChanged;
        _hooks?.UninstallHooks();
        _hooks = null;
        _udp?.Disconnect();
        _switcher?.Dispose();
        _switcher = null;
        _switchCoordinator?.Dispose();
        _switchCoordinator = null;
        if (_layoutWindow != null)
        {
            _layoutWindow.Close();
            _layoutWindow = null;
        }
        IsConnected = false;
        IsPeerConfirmed = false;
        IsMouseSource = false;
        
        // Add file sharing cleanup
        CleanupFileSharing();
    }
    */

    /*
     * MAINWINDOW INTEGRATION
     * ======================
     * 
     * In MainWindow.xaml, add AllowDrop attribute:
     */

    /*
    <Window x:Class="OmniMouse.MainWindow"
            ...
            AllowDrop="True"
            Drop="Window_Drop"
            DragEnter="Window_DragEnter">
    */

    /*
     * In MainWindow.xaml.cs:
     */

    /*
    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        // Get the HomePage user control
        var homePage = this.FindName("HomePageControl") as OmniMouse.Views.UserControls.HomePage;
        if (homePage?.DataContext is HomePageViewModel vm)
        {
            vm.HandleFileDrop(e);
        }
    }
    */

    /*
     * OPTIONAL: ADD A BUTTON TO SHOW FILE SHARE WINDOW
     * =================================================
     * 
     * In HomePage.xaml, add a button:
     */

    /*
    <Button Content="File Sharing"
            Command="{Binding ShowFileShareCommand}"
            Margin="5"
            Padding="10,5"/>
    */

    /*
     * In HomePageViewModel, add the command:
     */

    /*
    public ICommand ShowFileShareCommand { get; }

    // In constructor:
    ShowFileShareCommand = new RelayCommand(_ => ShowFileShareWindow(), _ => _udp != null);
    */
}
