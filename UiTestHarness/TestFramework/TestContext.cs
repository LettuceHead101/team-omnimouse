using System;
using FlaUI.Core;
using FlaUI.UIA3;
using FlaUI.Core.AutomationElements;

namespace UiTestHarness.TestFramework
{
    /// <summary>
    /// TestContext holds the shared runtime objects a test needs:
    /// - Application: process wrapper from FlaUI
    /// - UIA3Automation: the automation backend object to query UIA
    /// - MainWindow: the Window object representing the app's main window
    /// TestContext implements IDisposable to ensure automation and process are cleaned up.
    /// </summary>
    public sealed class TestContext : IDisposable
    {
        public Application App { get; }
        public UIA3Automation Automation { get; }
        public Window MainWindow { get; }

        public TestContext(Application app, UIA3Automation automation, Window mainWindow)
        {
            App = app;
            Automation = automation;
            MainWindow = mainWindow;
        }

        // Dispose should gracefully close the app and free automation resources.
        public void Dispose()
        {
            try { Automation.Dispose(); } catch { }
            try { App.Close(); } catch { try { App.Kill(); } catch { } }
        }
    }
}
