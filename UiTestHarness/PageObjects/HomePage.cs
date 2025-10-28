using System;
using FlaUI.Core.AutomationElements;
using FlaUI.Core;

namespace UiTestHarness.PageObjects
{
    // Page object pattern: encapsulate selectors and common UI operations for the HomePage.
    // Tests call these high-level methods rather than interacting with FlaUI directly.
    public sealed class HomePage
    {
        private readonly Window _window;

        // Constructor: capture the Window instance for queries
        public HomePage(Window window) => _window = window ?? throw new ArgumentNullException(nameof(window));

        // Typed accessors that search by AutomationId added to the WPF XAML.
        // They are nullable because the element may not exist (tests should handle that).
        public TextBox? HostIpBox => _window.FindFirstDescendant(cf => cf.ByAutomationId("HostIpBox"))?.AsTextBox();
        public Button? DisconnectButton => _window.FindFirstDescendant(cf => cf.ByAutomationId("DisconnectButton"))?.AsButton();
        public TextBox? ConsoleOutputBox => _window.FindFirstDescendant(cf => cf.ByAutomationId("ConsoleOutputBox"))?.AsTextBox();
        public Button? ConnectButton => _window.FindFirstDescendant(cf => cf.ByAutomationId("ConnectButton"))?.AsButton();

        // High-level operations with defensive checks:

        // Set the Host IP textbox value.
        public void SetHostIp(string ip)
        {
            var tb = HostIpBox ?? throw new InvalidOperationException("HostIpBox not found in UI.");
            tb.Text = ip ?? string.Empty;
        }

        // Click the Connect button
        public void ClickConnect()
        {
            var btn = ConnectButton ?? throw new InvalidOperationException("ConnectButton not found in UI.");
            if (!btn.IsEnabled) throw new InvalidOperationException("ConnectButton is disabled.");
            btn.Invoke();
        }

        // Click the Disconnect button
        public void ClickDisconnect()
        {
            var btn = DisconnectButton ?? throw new InvalidOperationException("DisconnectButton not found in UI.");
            if (!btn.IsEnabled) throw new InvalidOperationException("DisconnectButton is disabled.");
            btn.Invoke();
        }

        // Read the ConsoleOutput textbox content safely
        public string GetConsoleText() => ConsoleOutputBox?.Text ?? string.Empty;
    }
}
