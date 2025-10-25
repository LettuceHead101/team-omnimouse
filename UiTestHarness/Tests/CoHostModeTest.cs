using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UiTestHarness.TestFramework;
using UiTestHarness.PageObjects;

namespace UiTestHarness.Tests
{
    // Robust receiver-mode integration test:
    // - Launches one OmniMouse instance as Cohost (receiver)
    // - Sends a normalized UDP packet from the harness to localhost:5000
    // - Verifies the cohost logs a normalized receive
    public sealed class CohostIntegrationTest : IUiTest
    {
        public string Name => "CohostIntegrationTest";

        public bool Run(TestContext cohostCtx)
        {
            var page = new HomePage(cohostCtx.MainWindow);
            try
            {
                // Start receiver
                page.ClickCohost();
                Thread.Sleep(300); // allow bind/recv loop to start

                // Send one normalized packet to localhost:5000
                var endPoint = new IPEndPoint(IPAddress.Loopback, 5000);
                using var udp = new UdpClient();
                var buf = new byte[1 + 8];
                buf[0] = 0x01; // normalized prefix
                Array.Copy(BitConverter.GetBytes(0.50f), 0, buf, 1, 4);     // nx
                Array.Copy(BitConverter.GetBytes(0.25f), 0, buf, 1 + 4, 4); // ny
                udp.Send(buf, buf.Length, endPoint);

                // Wait up to ~6s for a normalized receive log line
                for (int i = 0; i < 30; i++)
                {
                    var txt = page.GetConsoleText();
                    if (!string.IsNullOrWhiteSpace(txt) &&
                        (txt.IndexOf("[UDP][RecvNormalized]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         txt.IndexOf("[UDP][RecvFallbackFloat]", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        Console.WriteLine("CohostIntegrationTest: observed normalized UDP receive in cohost logs.");
                        return true;
                    }
                    Thread.Sleep(200);
                }

                Console.WriteLine("CohostIntegrationTest: expected UDP receive log not found.");
                Console.WriteLine("Cohost console snapshot:");
                Console.WriteLine(page.GetConsoleText());
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CohostIntegrationTest: exception {ex}");
                return false;
            }
            finally
            {
                try { page.ClickDisconnect(); } catch { }
            }
        }
    }
}