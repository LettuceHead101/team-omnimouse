using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using FlaUI.Core;
using FlaUI.UIA3;
using FlaUI.Core.AutomationElements; // used for Window type

namespace UiTestHarness.TestFramework
{
    public static class AppLauncher
    {
        // LocateExe: try override first, then walk up folder tree to find repo root,
        // search for OmniMouse.exe under the repo, fall back to a common build path.
        public static string? LocateExe(string? overridePath)
        {
            // If caller supplied a valid path, use it immediately (explicit override)
            if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
                return Path.GetFullPath(overridePath);

            // Start from the harness runtime folder (bin\Debug\net8.0-windows\...) and
            // walk up parents to find a repository or solution root.
            var baseDir = AppContext.BaseDirectory;
            var dir = new DirectoryInfo(baseDir);
            DirectoryInfo? repoRoot = dir;
            while (repoRoot != null)
            {
                // Heuristic checks for repo root: OmniMouse folder, .sln file or OmniMouse.csproj
                if (Directory.Exists(Path.Combine(repoRoot.FullName, "OmniMouse"))
                    || repoRoot.GetFiles("*.sln", SearchOption.TopDirectoryOnly).Any()
                    || File.Exists(Path.Combine(repoRoot.FullName, "OmniMouse.csproj")))
                {
                    break;
                }
                repoRoot = repoRoot.Parent;
            }

            // solutionRoot: either found repoRoot or a conservative fallback path
            var solutionRoot = repoRoot?.FullName ?? Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", ".."));

            try
            {
                // Search for OmniMouse.exe under the solution root and prefer matches under OmniMouse\bin
                var matches = Directory.EnumerateFiles(solutionRoot, "OmniMouse.exe", SearchOption.AllDirectories)
                                       .Where(p => p.IndexOf(Path.Combine("OmniMouse", "bin"), StringComparison.OrdinalIgnoreCase) >= 0)
                                       .ToList();
                if (matches.Any())
                    return matches.First();
            }
            catch
            {
                // swallow exceptions — caller will get null and decide what to do
            }

            // Common fallback: typical debug build output path
            var fallback = Path.Combine(solutionRoot, "OmniMouse", "bin", "Debug", "net8.0-windows", "OmniMouse.exe");
            return File.Exists(fallback) ? fallback : null;
        }

        // Launch: start the app and wait for its main window. Returns an initialized TestContext.
        public static TestContext Launch(string exePath, int windowWaitMs = 10000)
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                throw new FileNotFoundException("OmniMouse.exe not found", exePath);

            // Use ProcessStartInfo so the launched app has a sensible working directory (its folder)
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
            };

            // Launch via FlaUI Application wrapper so we can attach automation easily
            var app = Application.Launch(startInfo);
            var automation = new UIA3Automation();

            // Wait for the main window to become available (simple polling loop)
            Window? mainWindow = null;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < windowWaitMs)
            {
                mainWindow = app.GetMainWindow(automation);
                if (mainWindow != null && mainWindow.IsEnabled) break;
                System.Threading.Thread.Sleep(200);
            }
            if (mainWindow == null)
            {
                // On failure, try to clean up process / automation
                try { automation.Dispose(); } catch { }
                try { app.Kill(); } catch { }
                throw new Exception("Failed to find main window after launching application.");
            }

            // Wrap runtime objects in TestContext for tests to use
            return new TestContext(app, automation, mainWindow);
        }
    }
}
