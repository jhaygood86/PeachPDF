using System.Diagnostics;

namespace PeachDrawing.Text.Tests
{
    /// <summary>
    /// What the robustness tests assert about time and space. A font is untrusted input, and what they prove is that no font makes the engine
    /// run or allocate without bound: the interpreters stop a program at a budget of their own, and the answer is a controlled failure.
    /// </summary>
    /// <remarks>
    /// The bound is on <b>one case</b> (one font, one glyph, one program), never on a whole sweep. A sweep is as long as its number of cases, and how long
    /// that takes depends on the runner, on coverage instrumentation and on the load of the machine: a budget of a hundred seconds over thousands of cases
    /// passed in about three seconds on a development machine and failed once at 140 s on a slow instrumented runner, without any case having gone wrong.
    /// The time of one case has a huge margin instead: the slowest takes a fraction of a second, and one that a budget did not stop would take
    /// minutes to hours (or for ever), so a limit of thirty seconds tells the two apart on any machine. Space is bounded the same way, in bytes
    /// allocated by the thread, which does not depend on the speed of the machine at all.
    /// </remarks>
    internal static class WorkBounds
    {
        /// <summary>The longest one case may take.</summary>
        public static readonly TimeSpan PerCase = TimeSpan.FromSeconds(30);

        /// <summary>The most one case may allocate on the thread that runs it.</summary>
        public const long PerCaseAllocation = 256L * 1024 * 1024;

        /// <summary>Runs one case and asserts that it ended within the bounds of a case.</summary>
        public static void Case(Action action, string? what = null)
        {
            Case(() =>
            {
                action();
                return 0;
            }, what);
        }

        /// <summary>Runs one case, gives what it returns, and asserts that it ended within the bounds of a case.</summary>
        public static T Case<T>(Func<T> function, string? what = null)
        {
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();

            T result = function();

            var elapsed = watch.Elapsed;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            string label = what is null ? "" : what + ": ";

            Assert.True(elapsed < PerCase, $"{label}a case took {elapsed.TotalSeconds:F1} s, the bound of one case is {PerCase.TotalSeconds:F0} s");
            Assert.True(allocated < PerCaseAllocation, $"{label}a case allocated {allocated / 1024 / 1024} MB, the bound of one case is {PerCaseAllocation / 1024 / 1024} MB");
            return result;
        }
    }
}
