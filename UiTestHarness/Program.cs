using System;
using System.Collections.Generic;
using UiTestHarness.TestFramework;
using UiTestHarness.Tests;

namespace UiTestHarness
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // Runner entry: brief startup message
            Console.WriteLine("UiTestHarness starting...");

            // Accept an explicit path to OmniMouse.exe as the first CLI argument
            string? exePath = null;
            if (args != null && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
                exePath = args[0];

            // Find the executable using AppLauncher heuristics (or the provided path)
            var located = AppLauncher.LocateExe(exePath);
            if (located is null)
            {
                Console.WriteLine("OmniMouse.exe not found. Build OmniMouse project or pass path as first argument.");
                return 2; // non-zero indicates failure to set up
            }

            Console.WriteLine($"Using OmniMouse.exe at: {located}");

            // Build list of tests. Each IUiTest is self-contained and will be run with a fresh app instance.
            var tests = new List<IUiTest>
            {
                new CohostIntegrationTest(), // integration: launches two instances and verifies UDP delivery
                new HostModeTest()           // simple host-mode UI test
            };

            int passed = 0;
            foreach (var t in tests)
            {
                Console.WriteLine($"Running test: {t.Name}");

                // Launch a fresh application instance for test isolation.
                // Using a new TestContext per test prevents previous-test state from affecting later tests.
                using var ctx = AppLauncher.Launch(located);
                bool ok = t.Run(ctx);

                Console.WriteLine($"Test {t.Name} => {(ok ? "PASS" : "FAIL")}");
                if (ok) passed++;
            }

            Console.WriteLine($"Tests complete. Passed {passed}/{tests.Count}");
            // Exit code 0 when all pass, 1 otherwise (simple CI-friendly contract)
            return passed == tests.Count ? 0 : 1;
        }
    }
}