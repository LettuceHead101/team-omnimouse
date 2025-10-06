using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlaUI.Core;

namespace UiTestHarness.TestFramework
{
    /// <summary>
    /// IUiTest is the small contract tests implement so the runner can execute them uniformly.
    /// - Name: friendly test name used in logging
    /// - Run(TestContext): performs the test using the provided TestContext (returns true on success)
    /// </summary>
    public interface IUiTest
    {
        /// <summary>
        /// Run the test using the provided context. Return true on success.
        /// </summary>
        bool Run(TestContext context);
        string Name { get; }
    }
}
