using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace UiTestHarness
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // Top-level log so you know the harness started.
            Console.WriteLine("UiTestHarness starting...");

            // Allow caller to pass a full path to OmniMouse.exe as the first argument.
            // If provided, we will use that path and skip the search logic below.
            string? exePath = null;
            if (args != null && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            {
                exePath = args[0];
            }

            // Determine a reasonable search root for the repository by walking up
            // from the harness runtime directory (bin\Debug\net8.0-windows\...).
            var baseDir = AppContext.BaseDirectory;
            Console.WriteLine($"Runtime base dir: {baseDir}");
            var dir = new DirectoryInfo(baseDir);

            // Walk up parent directories looking for a repo/solution root heuristic:
            // - contains an "OmniMouse" folder, or
            // - has a .sln file, or
            // - contains an OmniMouse.csproj file
            DirectoryInfo? repoRoot = dir;
            while (repoRoot != null)
            {
                if (Directory.Exists(Path.Combine(repoRoot.FullName, "OmniMouse"))
                    || repoRoot.GetFiles("*.sln", SearchOption.TopDirectoryOnly).Any()
                    || File.Exists(Path.Combine(repoRoot.FullName, "OmniMouse.csproj")))
                {
                    // Found a candidate repo root; stop walking up.
                    break;
                }
                repoRoot = repoRoot.Parent;
            }

            // If walking failed, fallback to a conservative relative path from baseDir.
            var solutionRoot = repoRoot?.FullName ?? Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));
            Console.WriteLine($"Determined search root: {solutionRoot}");

            // If an exePath wasn't supplied, search the solutionRoot for OmniMouse.exe.
            if (exePath is null)
            {
                try
                {
                    // Find files named OmniMouse.exe under the solution root.
                    // We filter to paths that include "OmniMouse\bin" to reduce false positives.
                    var matches = Directory.EnumerateFiles(solutionRoot, "OmniMouse.exe", SearchOption.AllDirectories)
                                           .Where(p => p.IndexOf(Path.Combine("OmniMouse", "bin"), StringComparison.OrdinalIgnoreCase) >= 0)
                                           .ToList();
                    if (matches.Count > 0)
                    {
                        // Print a few candidates so you can inspect them when debugging.
                        Console.WriteLine("Candidates found:");
                        foreach (var m in matches.Take(10)) Console.WriteLine("  " + m);

                        // Use the first match by default.
                        exePath = matches.First();
                    }
                    else
                    {
                        Console.WriteLine("No matches found under search root.");
                    }
                }
                catch (Exception ex) when (ex is DirectoryNotFoundException || ex is UnauthorizedAccessException)
                {
                    // Directory enumeration can fail due to permissions or missing folders.
                    Console.WriteLine($"Search failed: {ex.GetType().Name}: {ex.Message}");
                }

                // If search returned nothing, check a common fallback location:
                // OmniMouse\bin\Debug\net8.0-windows\OmniMouse.exe
                if (exePath is null)
                {
                    var fallback = Path.Combine(solutionRoot, "OmniMouse", "bin", "Debug", "net8.0-windows", "OmniMouse.exe");
                    Console.WriteLine($"Checking fallback: {fallback}");
                    if (File.Exists(fallback))
                        exePath = fallback;
                }
            }

            // If we still don't have a valid exe path, bail out with an informative message.
            if (exePath is null || !File.Exists(exePath))
            {
                Console.WriteLine("OmniMouse.exe not found. Build OmniMouse project first or pass full path as first argument.");
                Console.WriteLine("You can also run the harness with the full path: UiTestHarness.exe \"C:\\path\\to\\OmniMouse.exe\"");
                return 2;
            }

            // Log the path we'll launch.
            Console.WriteLine($"Launching '{exePath}'");

            // Prepare ProcessStartInfo so the launched app has the correct working directory.
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? solutionRoot
            };

            // Launch the target application using FlaUI's Application wrapper.
            // Application.Launch returns an IDisposable that ties into the automation lifetime.
            using var app = Application.Launch(startInfo);

            // Create the UIA3 automation instance (UI Automation backend).
            using var automation = new UIA3Automation();

            try
            {
                // Wait for the main window to be available and enabled.
                // Looping handles startup latency; adjust iteration count / sleep if needed.
                Window? mainWindow = null;
                for (int i = 0; i < 50; i++)
                {
                    mainWindow = app.GetMainWindow(automation);
                    if (mainWindow != null && mainWindow.IsEnabled) break;
                    Thread.Sleep(200);
                }
                if (mainWindow == null)
                    throw new Exception("Failed to find OmniMouse main window");

                Console.WriteLine($"Found main window: '{mainWindow.Title}'");

                // Locate controls by AutomationId. These are stable identifiers you added to XAML.
                // AsTextBox / AsButton convert the generic AutomationElement into a typed wrapper
                // that exposes convenient properties and methods.
                var hostIpBox = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("HostIpBox"))?.AsTextBox()
                                ?? throw new Exception("HostIpBox not found");
                var hostButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("HostButton"))?.AsButton()
                                 ?? throw new Exception("HostButton not found");
                var consoleOutput = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ConsoleOutputBox"))?.AsTextBox()
                                    ?? throw new Exception("ConsoleOutputBox not found");

                // Interact with the UI: set the Host IP textbox value.
                Console.WriteLine("Setting Host IP to 127.0.0.1");
                hostIpBox.Text = "127.0.0.1";
                // Small pause to allow bindings/validation to propagate.
                Thread.Sleep(200);

                // Invoke (click) the Host button.
                Console.WriteLine("Clicking Host");
                hostButton.Invoke();

                // Poll the console output textbox until it contains an expected substring,
                // or until timeout. This verifies the ViewModel/UI reacted as expected.
                for (int t = 0; t < 25; t++)
                {
                    var text = consoleOutput.Text ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(text) && text.IndexOf("Starting Host", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Console.WriteLine("Observed expected log in ConsoleOutput.");
                        break;
                    }
                    Thread.Sleep(200);
                }

                // Print the final snapshot of the ConsoleOutputBox so you can inspect logs.
                Console.WriteLine("Snapshot of ConsoleOutput:");
                Console.WriteLine("---- BEGIN ----");
                Console.WriteLine(consoleOutput.Text ?? string.Empty);
                Console.WriteLine("----  END  ----");

                // Graceful shutdown: give the app a moment, then request it to close.
                Console.WriteLine("Harness finished — closing app in 2s.");
                Thread.Sleep(2000);

                try { app.Close(); } // try graceful close
                catch { app.Kill(); } // fallback to force kill if close fails

                return 0;
            }
            catch (Exception ex)
            {
                // If anything goes wrong, print the exception and attempt to kill
                // the launched process to avoid orphaned apps.
                Console.WriteLine("Fatal: " + ex);
                try { app.Kill(); } catch { }
                return 1;
            }
        }
    }
}