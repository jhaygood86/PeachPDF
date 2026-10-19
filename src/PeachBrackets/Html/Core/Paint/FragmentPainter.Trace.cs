#if DEBUG
using PeachDrawing.Core;
using PeachPDF.Html.Core.Fragments;
using System;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// Debug-build diagnostic: one line per page and per word the painter visits, saying where the word is on
    /// its page and how much of it the page clip leaves visible.
    /// </summary>
    /// <remarks>
    /// Text extraction cannot see a word that was painted but cut by the page clip (a line straddling the
    /// foot, a word at the right edge), and that is exactly the shape of a "lost" word: the trace tells a word
    /// that was never drawn from one that was drawn partly outside its page. Off unless
    /// <c>PEACHPDF_TRACE_PAINT=1</c> is set in the environment, because it writes a line per word - the
    /// <c>paint:</c> line in <see cref="PaintFragment"/> is the coarse view of the same walk, printed by every
    /// Debug build. The <c>ClipReport</c> kept on the container in every build lists only the words that were
    /// drawn and cut; this trace also lists the words skipped outright and the ones shown whole.
    /// <see cref="TraceSink"/> is per painter so a test can capture the lines without redirecting the
    /// console, which every test running in parallel would share.
    /// </remarks>
    internal sealed partial class FragmentPainter
    {
        private static readonly bool TracePaintFromEnvironment = Environment.GetEnvironmentVariable("PEACHPDF_TRACE_PAINT") == "1";

        /// <summary>
        /// Where the trace lines go: the console when <c>PEACHPDF_TRACE_PAINT=1</c>, nowhere otherwise.
        /// </summary>
        internal Action<string>? TraceSink { get; init; } = TracePaintFromEnvironment ? Console.WriteLine : null;

        private int _tracePage = -1;

        private void TracePage(FragmentainerFragment fragmentainer, Rect pageClip)
        {
            if (TraceSink is not { } sink) return;

            _tracePage = -1;

            if (container.FragmentTree is { } tree)
            {
                for (var i = 0; i < tree.Fragmentainers.Count; i++)
                {
                    if (ReferenceEquals(tree.Fragmentainers[i], fragmentainer))
                    {
                        _tracePage = i;
                        break;
                    }
                }
            }

            sink(FormattableString.Invariant($"paintpage: {_tracePage} clip=({pageClip.X:F1},{pageClip.Y:F1},{pageClip.Width:F1}x{pageClip.Height:F1})"));
        }

        private void TraceWord(TextFragment wordFragment, Rect visible, bool drawn)
        {
            if (TraceSink is not { } sink) return;

            var word = wordFragment.Word;
            var rect = wordFragment.Rect;

            var state = drawn ? "drawn" : "SKIPPED";

            sink(FormattableString.Invariant(
                $"paintword: p{_tracePage} {word.FirstLineText ?? word.Text} rect=({rect.X:F1},{rect.Y:F1},{rect.Width:F1}x{rect.Height:F1}) visible={visible.Width:F1}x{visible.Height:F1} {state}"));
        }
    }
}
#endif
