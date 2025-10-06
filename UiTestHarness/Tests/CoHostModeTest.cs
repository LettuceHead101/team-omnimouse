using System;
using System.Runtime.InteropServices;
using System.Threading;
using UiTestHarness.TestFramework;
using UiTestHarness.PageObjects;

namespace UiTestHarness.Tests
{
    // Integration test that runs a receiver and a sender instance and asserts UDP traffic is received.
    public sealed class CohostIntegrationTest : IUiTest
    {
        public string Name => "CohostIntegrationTest";

        // Move the system cursor — used to generate low-level mouse events the host captures.
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        public bool Run(TestContext hostCtx)
        {
            // Locate the exe (AppLauncher handles repo heuristics)
            string? exe = AppLauncher.LocateExe(null);
            if (exe is null)
            {
                Console.WriteLine("CohostIntegrationTest: cannot locate OmniMouse.exe");
                return false;
            }

            TestContext? cohostCtx = null;
            try
            {
                // Wrap the main window of the host instance in a page object.
                var hostPage = new HomePage(hostCtx.MainWindow);

                // Launch a second application instance to act as the cohost (receiver).
                cohostCtx = AppLauncher.Launch(exe, windowWaitMs: 10000);
                var cohostPage = new HomePage(cohostCtx.MainWindow);

                // Start cohost (receiver) so it begins listening for UDP packets.
                cohostPage.ClickCohost();
                Thread.Sleep(500); // allow receiver to initialize

                // Configure the host instance to send to the local cohost.
                hostPage.SetHostIp("127.0.0.1");
                Thread.Sleep(150);

                // Start host (sender)
                hostPage.ClickHost();
                Thread.Sleep(500); // allow hooks and UDP to initialize

                // Compute a point inside the host window and move the global cursor there.
                var rect = hostCtx.MainWindow.BoundingRectangle;
                int targetX = (int)((rect.Left + rect.Right) / 2);
                int targetY = (int)((rect.Top + rect.Bottom) / 2);

                Console.WriteLine($"Moving cursor to ({targetX},{targetY}) to trigger mouse move...");
                SetCursorPos(targetX, targetY);

                // Poll the cohost's ConsoleOutputBox for evidence of UDP receive ("Moving cursor to" or "[UDP][Receive]")
                for (int i = 0; i < 30; i++)
                {
                    var txt = cohostPage.GetConsoleText();
                    if (!string.IsNullOrWhiteSpace(txt) &&
                        (txt.IndexOf("Moving cursor to", StringComparison.OrdinalIgnoreCase) >= 0
                         || txt.IndexOf("[UDP][Receive]", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        Console.WriteLine("CohostIntegrationTest: observed UDP receive in cohost logs.");
                        return true;
                    }
                    Thread.Sleep(200);
                }

                // Failure case: print cohost console snapshot for debugging.
                Console.WriteLine("CohostIntegrationTest: did not observe expected UDP receive in cohost logs.");
                Console.WriteLine("Cohost console snapshot:");
                Console.WriteLine(cohostPage.GetConsoleText());
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CohostIntegrationTest: exception {ex}");
                return false;
            }
            finally
            {
                // Ensure the cohost process is closed regardless of test result.
                try { cohostCtx?.Dispose(); } catch { }
            }
        }
    }
}