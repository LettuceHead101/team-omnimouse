using System;
using System.Collections.Generic;
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
        private PeerDiscovery? _discovery;
        private string _consoleOutput = string.Empty;
        private string _hostIp = string.Empty;
        private bool _isConnected;

        private readonly List<string> _consoleLines = new();
        private const int MaxConsoleLines = 20;

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

        public bool CanConnect => !IsConnected;
        public bool CanDisconnect => IsConnected;

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }

        public HomePageViewModel()
        {
            ConnectCommand = new RelayCommand(ExecuteConnect, CanExecuteConnect);
            DisconnectCommand = new RelayCommand(ExecuteDisconnect, CanExecuteDisconnect);

            App.ConsoleOutputReceived += OnConsoleOutputReceived;
        }

        private void ExecuteConnect(object? parameter)
        {
            if (string.IsNullOrWhiteSpace(HostIp))
            {
                WriteToConsole("Enter the peer's IP before connecting.");
                return;
            }

            WriteToConsole($"Connecting to peer at {HostIp} (bidirectional)...");
            _udp = new UdpMouseTransmitter();
            _udp.StartPeer(HostIp);
            StartHooks(_udp);
            IsConnected = true;
        }

        private void ExecuteDisconnect(object? parameter)
        {
            WriteToConsole("Disconnecting session...");
            _hooks?.UninstallHooks();
            _udp?.Disconnect();
            _hooks = null;
            _udp = null;
            IsConnected = false;
        }

        private bool CanExecuteConnect(object? parameter) => !IsConnected;
        private bool CanExecuteDisconnect(object? parameter) => IsConnected;

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
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                if (string.IsNullOrEmpty(text)) return;

                var normalized = text.Replace("\r", "");
                var parts = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in parts)
                {
                    _consoleLines.Add(line);
                    while (_consoleLines.Count > MaxConsoleLines)
                        _consoleLines.RemoveAt(0);
                }

                ConsoleOutput = string.Join(Environment.NewLine, _consoleLines);
            }));
        }

        private void UpdateButtonStates()
        {
            CommandManager.InvalidateRequerySuggested();
            OnPropertyChanged(nameof(CanConnect));
            OnPropertyChanged(nameof(CanDisconnect));
        }

        public void Cleanup()
        {
            App.ConsoleOutputReceived -= OnConsoleOutputReceived;
            _hooks?.UninstallHooks();
            _udp?.Disconnect();
            _hooks = null;
            _udp = null;
        }
    }

    public sealed class RelayCommand : ICommand
    {
        private static readonly Predicate<object?> s_true = _ => true;
        private readonly Action<object?> _execute;
        private readonly Predicate<object?> _canExecute;

        public RelayCommand(Action<object?> execute)
            : this(execute, s_true) { }

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute ?? s_true;
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