using System;
using System.IO;
using System.Text;
using System.Windows;

namespace OmniMouse
{
    public partial class App : Application
    {
        public static event Action<string>? ConsoleOutputReceived;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Console.SetOut(new ConsoleRedirector());
            Console.WriteLine("OmniMouse started. Waiting for input...");
        }

        private class ConsoleRedirector : TextWriter
        {
            public override Encoding Encoding => Encoding.UTF8;

            public override void WriteLine(string? value)
            {
                ConsoleOutputReceived?.Invoke((value ?? "") + Environment.NewLine);
            }

            public override void Write(char value)
            {
                ConsoleOutputReceived?.Invoke(value.ToString());
            }

            public override void Write(string? value)
            {
                if (!string.IsNullOrEmpty(value))
                    ConsoleOutputReceived?.Invoke(value);
            }
        }
    }
}