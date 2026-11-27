using System;
using System.Diagnostics;
using System.Threading;

namespace PeachBrackets.Html.Paint
{
    /// <summary>
    /// Optional accounting of what one page paint walked, written when the environment variable <c>PEACH_PAINT_STATS</c> is set (the same switch as the
    /// canvas backends'; <c>PEACH_PAINT_STATS_FILE</c> appends to a file): per <c>FragmentPainter.Paint</c>, how many fragments the page holds against
    /// how many the paint entered, how many nodes the stacking-order search and the escaping-descendant check walked, and how long the paint-reach
    /// pre-pass and the whole paint took. The first three show work that scales with the page rather than with the region being painted.
    /// </summary>
    internal static class PaintStatistics
    {
        /// <summary>Whether the statistics are kept: the environment variable, unless a test (or a host that wants the figures) changes it.</summary>
        internal static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("PEACH_PAINT_STATS") is { Length: > 0 };

        /// <summary>Told each line instead of the debugger output and the file, when set.</summary>
        internal static Action<string>? Sink { get; set; }

        // Per thread: a page is painted by one thread from start to end, and a count kept for the whole process was zeroed by whichever paint on another thread reported first
        // (the figures of a paint that ran beside another were the other's, or nothing).
        [ThreadStatic]
        internal static long ReachNodes;

        [ThreadStatic]
        internal static long FragmentsEntered;

        [ThreadStatic]
        internal static long HoistSearchNodes;

        [ThreadStatic]
        internal static long EscapeCheckNodes;

        internal static long Start()
        {
            if (!Enabled)
                return 0;

            // What this thread counted before the paint began (a layout, a paint of another kind) is not the paint's.
            ReachNodes = FragmentsEntered = HoistSearchNodes = EscapeCheckNodes = 0;
            return Stopwatch.GetTimestamp();
        }

        internal static double MillisecondsSince(long started) => (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

        internal static void Add(ref long counter)
        {
            if (Enabled)
                counter++;
        }

        /// <summary>One line of free text in the same stream (why a restyle was not applied in place, say), when the statistics are on.</summary>
        internal static void Note(string text)
        {
            if (Enabled)
                Write("PEACH_PAINT_STATS(note) " + text);
        }

        internal static void Report(double reachMs, double totalMs) =>
            Write($"PEACH_PAINT_STATS(engine) pageFragments={Take(ref ReachNodes)} entered={Take(ref FragmentsEntered)} " +
                $"hoistSearchNodes={Take(ref HoistSearchNodes)} escapeCheckNodes={Take(ref EscapeCheckNodes)} reach={reachMs:F2}ms paint={totalMs:F2}ms");

        private static long Take(ref long counter)
        {
            var value = counter;
            counter = 0;
            return value;
        }

        private static void Write(string line)
        {
            if (Sink is { } sink)
            {
                sink(line);
                return;
            }

            Debug.WriteLine(line);
            if (Environment.GetEnvironmentVariable("PEACH_PAINT_STATS_FILE") is { Length: > 0 } file)
            {
                // A diagnostic never fails the paint it is describing: a file another process (or a reader of it) holds open just loses the line.
                try
                {
                    lock (typeof(PaintStatistics))
                        System.IO.File.AppendAllText(file, line + Environment.NewLine);
                }
                catch (System.IO.IOException)
                {
                }
            }
        }
    }
}
