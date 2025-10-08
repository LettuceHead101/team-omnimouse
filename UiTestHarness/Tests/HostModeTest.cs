using System;
using UiTestHarness.TestFramework;
using UiTestHarness.PageObjects;

namespace UiTestHarness.Tests
{
    // Small deterministic test that exercises the Host flow in the UI.
    // It uses the HomePage page object rather than raw FlaUI calls.
    public sealed class HostModeTest : IUiTest
    {
        public string Name => "HostModeTest";

        public bool Run(TestContext context)
        {
            var page = new HomePage(context.MainWindow);

            try
            {
                // Enter the host IP (we use localhost here for safety)
                page.SetHostIp("127.0.0.1");

                // Short pause to let binding propagate (replaceable later by WaitHelper)
                System.Threading.Thread.Sleep(150);

                // Click Host button to start sender hooks+UDP
                page.ClickHost();

                // Poll ConsoleOutputBox for the expected sign that host started.
                // This loop checks up to ~5 seconds (25 * 200ms)
                for (int i = 0; i < 25; i++)
                {
                    var txt = page.GetConsoleText();
                    if (!string.IsNullOrWhiteSpace(txt) && txt.IndexOf("Starting Host", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true; // success condition observed
                    System.Threading.Thread.Sleep(200);
                }

                // If loop completes, the expected log wasn't found — test fails.
                Console.WriteLine("HostModeTest: expected log not found.");
                return false;
            }
            catch (Exception ex)
            {
                // Any exception is treated as test failure; print details for debugging.
                Console.WriteLine($"HostModeTest: exception {ex}");
                return false;
            }
            finally
            {
                // Best-effort cleanup to return the app to a neutral state for later tests.
                try { page.ClickDisconnect(); } catch { }
            }
        }
    }
}
