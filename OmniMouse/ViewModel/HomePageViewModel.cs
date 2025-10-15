using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Threading;
using OmniMouse.Hooks;
using OmniMouse.MVVM;
using OmniMouse.Network;

namespace OmniMouse.ViewModel
{
    public class HomePageViewModel : ViewModelBase
    {
        private InputHooks? _hooks;
        private UdpMouseTransmitter? _udp;
        private bool _isSender;
        private string _consoleOutput = string.Empty;
        private string _hostIp = string.Empty;
        private bool _isConnected;

        // internal fixed-size line buffer to avoid unbounded growth
        private readonly List<string> _consoleLines = new();
        private const int MaxConsoleLines = 20;

        // Properties
        public string ConsoleOutput 
        { 
            get => _consoleOutput;
            set => SetProperty(ref _consoleOutput, value);
        }

        public string HostIp
        {
            get => _hostIp;
            set => SetProperty(ref _hostIp, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value, UpdateButtonStates);
        }

        // Button State Properties
        public bool CanConnect => !IsConnected;
        public bool CanDisconnect => IsConnected;

        // Commands
        public ICommand HostCommand { get; }
        public ICommand CohostCommand { get; }
        public ICommand DisconnectCommand { get; }

        public HomePageViewModel()
        {
            // Initialize commands
            HostCommand = new RelayCommand(ExecuteHost, CanExecuteHost);
            CohostCommand = new RelayCommand(ExecuteCohost, CanExecuteConnect);
            DisconnectCommand = new RelayCommand(ExecuteDisconnect, CanExecuteDisconnect);

            // subscribe to console output feed from App so we can show logs in UI
            App.ConsoleOutputReceived += OnConsoleOutputReceived;
        }

        // Command execution methods
        private void ExecuteHost(object? parameter)
        {
            if (string.IsNullOrWhiteSpace(HostIp))
            {
                WriteToConsole("Enter the Cohost's IP before starting Host mode.");
                return;
            }

            WriteToConsole($"Starting Host (sender). Sending to {HostIp}...");
            _udp = new UdpMouseTransmitter();
            _udp.StartCoHost(HostIp);
            StartHooks(_udp);
            _isSender = true;
            IsConnected = true;
        }

        private void ExecuteCohost(object? parameter)
        {
            WriteToConsole("Starting Cohost (receiver). Listening...");
            _udp = new UdpMouseTransmitter();
            _udp.StartHost();
            _isSender = false;
            IsConnected = true;
        }

        private void ExecuteDisconnect(object? parameter)
        {
            WriteToConsole("Disconnecting session...");
            _hooks?.UninstallHooks();
            _udp?.Disconnect();
            _hooks = null;
            _udp = null;
            _isSender = false;
            IsConnected = false;
        }

        // Command can-execute methods
        private bool CanExecuteHost(object? parameter) => !IsConnected;
        private bool CanExecuteConnect(object? parameter) => !IsConnected;
        private bool CanExecuteDisconnect(object? parameter) => IsConnected;

        // Helper methods
        private void StartHooks(UdpMouseTransmitter udp)
        {
            _hooks = new InputHooks(udp);
            _hooks.InstallHooks();
            var thread = new Thread(_hooks.RunMessagePump) { IsBackground = true };
            thread.Start();
        }

        private void WriteToConsole(string message)
        {
            Console.WriteLine(message);
        }

        private void OnConsoleOutputReceived(string text)
        {
            // Ensure UI updates happen on the UI thread and maintain only the last MaxConsoleLines lines.
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                if (string.IsNullOrEmpty(text)) return;

                // Normalize CRLF and split into lines
                var normalized = text.Replace("\r", "");
                var parts = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in parts)
                {
                    _consoleLines.Add(line);
                    // trim to last N lines
                    while (_consoleLines.Count > MaxConsoleLines)
                        _consoleLines.RemoveAt(0);
                }

                ConsoleOutput = string.Join(Environment.NewLine, _consoleLines);
            }));
        }

        private void UpdateButtonStates()
        {
            // Force command CanExecute to be re-evaluated
            CommandManager.InvalidateRequerySuggested();
            OnPropertyChanged(nameof(CanConnect));
            OnPropertyChanged(nameof(CanDisconnect));
        }

        // Cleanup
        public void Cleanup()
        {
            App.ConsoleOutputReceived -= OnConsoleOutputReceived;
            if (_isSender) _hooks?.UninstallHooks();
            _udp?.Disconnect();
            _hooks = null;
            _udp = null;
        }
    }

    // Simple ICommand implementation
    public sealed class RelayCommand : ICommand
    {
        private static readonly Predicate<object?> s_true = _ => true;
        private readonly Action<object?> _execute;
        private readonly Predicate<object?> _canExecute; // non-null

        public RelayCommand(Action<object?> execute)
            : this(execute, s_true) { }

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute ?? s_true; // always set
        }

        public bool CanExecute(object? parameter) => _canExecute(parameter);
        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }

}