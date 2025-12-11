using System;
using System.IO;
using System.Text;
using System.Windows;
using OmniMouse.Configuration;

namespace OmniMouse
{
    public partial class App : Application
    {
        public static AppSettings Settings { get; private set; } = new AppSettings();
        public static event Action<string>? ConsoleOutputReceived;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Settings = AppSettings.Load();
            Console.SetOut(new ConsoleRedirector());
            Console.WriteLine("OmniMouse started. Waiting for input...");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                Settings?.Save();
            }
            catch
            {
                // ignore errors on exit
            }
            base.OnExit(e);
        }

        private class ConsoleRedirector : TextWriter
        {
            private readonly StringBuilder _buffer = new();
            public override Encoding Encoding => Encoding.UTF8;

            public override void WriteLine(string? value)
            {
                if (value is null) value = string.Empty;
                _buffer.Append(value);
                _buffer.Append(Environment.NewLine);
                FlushBuffer();
            }

            public override void Write(char value)
            {
                _buffer.Append(value);
                if (value == '\n')
                    FlushBuffer();
            }

            public override void Write(string? value)
            {
                if (string.IsNullOrEmpty(value))
                    return;

                int start = 0;
                for (int i = 0; i < value.Length; i++)
                {
                    if (value[i] == '\n')
                    {
                        _buffer.Append(value.Substring(start, i - start + 1));
                        FlushBuffer();
                        start = i + 1;
                    }
                }
                if (start < value.Length)
                {
                    _buffer.Append(value.Substring(start));
                }
            }

            private void FlushBuffer()
            {       
                if (_buffer.Length == 0) return;
                var s = _buffer.ToString();
                _buffer.Clear();
                ConsoleOutputReceived?.Invoke(s);
            }
        }
    }
}