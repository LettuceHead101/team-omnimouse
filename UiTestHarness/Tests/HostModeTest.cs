using System;
using UiTestHarness.TestFramework;
using UiTestHarness.PageObjects;

namespace UiTestHarness.Tests
{
    // Updated: simple Connect smoke test against the new peer mode
    public sealed class HostModeTest : IUiTest
    {
        public string Name => "ConnectSmokeTest";

        public bool Run(TestContext context)
        {
            var page = new HomePage(context.MainWindow);

            try
            {
                page.SetHostIp("127.0.0.1");
                System.Threading.Thread.Sleep(150);

                page.ClickConnect();

                // Accept either the ViewModel log or UDP init log as success signal
                for (int i = 0; i < 30; i++)
                {
                    var txt = page.GetConsoleText();
                    if (!string.IsNullOrWhiteSpace(txt) &&
                        (txt.IndexOf("Connecting to peer", StringComparison.OrdinalIgnoreCase) >= 0
                         || txt.IndexOf("Peer mode bound", StringComparison.OrdinalIgnoreCase) >= 0
                         || txt.IndexOf("[UDP][SendNormalized]", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return true;
                    }
                    System.Threading.Thread.Sleep(200);
                }

                Console.WriteLine("ConnectSmokeTest: expected log not found.");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ConnectSmokeTest: exception {ex}");
                return false;
            }
            finally
            {
                try { page.ClickDisconnect(); } catch { }
            }
        }
    }
}
