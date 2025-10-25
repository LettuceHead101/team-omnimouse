using System;
using UiTestHarness.TestFramework;
using UiTestHarness.PageObjects;

namespace UiTestHarness.Tests
{
    // Updated: single-instance smoke test to avoid UDP port conflicts in peer mode
    public sealed class CohostIntegrationTest : IUiTest
    {
        public string Name => "ConnectLogSmokeTest";

        public bool Run(TestContext ctx)
        {
            var page = new HomePage(ctx.MainWindow);

            try
            {
                page.SetHostIp("127.0.0.1");
                System.Threading.Thread.Sleep(150);

                page.ClickConnect();

                // Look for any of these signals within ~6s
                for (int i = 0; i < 30; i++)
                {
                    var txt = page.GetConsoleText();
                    if (!string.IsNullOrWhiteSpace(txt) &&
                        (txt.IndexOf("Connecting to peer", StringComparison.OrdinalIgnoreCase) >= 0
                         || txt.IndexOf("Peer mode bound", StringComparison.OrdinalIgnoreCase) >= 0
                         || txt.IndexOf("[UDP][Recv", StringComparison.OrdinalIgnoreCase) >= 0
                         || txt.IndexOf("[UDP][SendNormalized]", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return true;
                    }
                    System.Threading.Thread.Sleep(200);
                }

                Console.WriteLine("ConnectLogSmokeTest: no expected log found.");
                Console.WriteLine(page.GetConsoleText());
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ConnectLogSmokeTest: exception {ex}");
                return false;
            }
            finally
            {
                try { page.ClickDisconnect(); } catch { }
            }
        }
    }
}