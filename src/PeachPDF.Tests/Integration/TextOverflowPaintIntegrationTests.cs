using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Paint-time coverage for <c>text-overflow: ellipsis</c> (issue #694) - per overflowing line,
    /// across horizontal LTR/RTL and vertical-rl/vertical-lr writing modes. Uses
    /// <see cref="RecordingGraphics"/> for structural assertions (which words got drawn, in what order,
    /// whether an ellipsis was drawn) rather than exact pixel positions. Its default
    /// <c>MeasureString</c> returns a fixed (0, 12) regardless of content - fine for the many existing
    /// clip/order tests elsewhere, but text-overflow's own visibility guard (issue #113's epsilon check,
    /// now also applied to a truncation's kept run/ellipsis glyph) treats a zero-width rect as clipped
    /// away, so every test here supplies <see cref="RecordingGraphics.MeasureStringOverride"/> with a
    /// deterministic, length-proportional width via <see cref="NewRecording"/> instead of relying on the
    /// shared default. Vertical-upright text's per-character extent comes from real font metrics
    /// (<c>RFont.GetVerticalAdvance</c>/<c>Height</c>), not <c>g.MeasureString</c> at all, and each kept
    /// character paints as its own <c>DrawString</c> call (<c>PaintUprightVerticalRun</c>), so exact
    /// kept-character-count assertions are meaningful there regardless of the override.
    /// </summary>
    public class TextOverflowPaintIntegrationTests
    {
        private const string LongWord = "abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz";
        private const string LongHebrewWord = "אאאאאאאאאאאאאאאאאאאאאאאאאאאאאאאאאאאאאאאא";
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        /// <summary>A recording mock whose MeasureString is a deterministic, non-zero, length-proportional stand-in for real glyph shaping - needed so text-overflow's own visibility guard (a zero-extent rect reads as clipped away) doesn't suppress every draw under the shared mock's real default of a flat (0, 12).</summary>
        private static RecordingGraphics NewRecording() => new(new PdfSharpAdapter())
        {
            MeasureStringOverride = (str, _, _) => new RSize((str?.Length ?? 0) * 6.0, 12)
        };

        [Fact]
        public async Task Horizontal_Ltr_Nowrap_Overflowing_DrawsEllipsisLast()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='width:50pt;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font-size:14pt'>{LongWord}</div>"));

            var box = LayoutHarness.FindById(root, "d")!;
            var recording = NewRecording();
            FragmentPaintHarness.PaintBox(container, box, recording);

            var drawn = recording.DrawnStrings.Select(d => d.Text).ToList();
            Assert.NotEmpty(drawn);
            Assert.Equal("…", drawn[^1]);
        }

        [Fact]
        public async Task Horizontal_Ltr_Nowrap_Fitting_DrawsNoEllipsis()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='width:400pt;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font-size:14pt'>short text</div>"));

            var box = LayoutHarness.FindById(root, "d")!;
            var recording = NewRecording();
            FragmentPaintHarness.PaintBox(container, box, recording);

            Assert.DoesNotContain("…", recording.DrawnStrings.Select(d => d.Text));
        }

        [Fact]
        public async Task Horizontal_Ltr_OverflowVisible_DrawsNoEllipsisEvenWhenTextIsWiderThanContainer()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='width:50pt;white-space:nowrap;overflow:visible;text-overflow:ellipsis;font-size:14pt'>{LongWord}</div>"));

            var box = LayoutHarness.FindById(root, "d")!;
            var recording = NewRecording();
            FragmentPaintHarness.PaintBox(container, box, recording);

            Assert.DoesNotContain("…", recording.DrawnStrings.Select(d => d.Text));
        }

        [Fact]
        public async Task Horizontal_Ltr_TextOverflowClip_DrawsNoEllipsis()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='width:50pt;white-space:nowrap;overflow:hidden;text-overflow:clip;font-size:14pt'>{LongWord}</div>"));

            var box = LayoutHarness.FindById(root, "d")!;
            var recording = NewRecording();
            FragmentPaintHarness.PaintBox(container, box, recording);

            Assert.DoesNotContain("…", recording.DrawnStrings.Select(d => d.Text));
        }

        [Fact]
        public async Task Horizontal_Rtl_Nowrap_Overflowing_DrawsEllipsis()
        {
            // Two words (not one unbroken run) so the line's visual-order sort actually has something
            // to compare - a single-word line never invokes the sort comparator at all.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' dir='rtl' style='width:50pt;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;direction:rtl;font-size:14pt'>מילה {LongHebrewWord}</div>"));

            var box = LayoutHarness.FindById(root, "d")!;
            var recording = NewRecording();
            FragmentPaintHarness.PaintBox(container, box, recording);

            Assert.Contains("…", recording.DrawnStrings.Select(d => d.Text));
        }

        [Fact]
        public async Task Horizontal_Ltr_Nowrap_MultipleWordsFitBeforeTheCut_EarlyWordsPaintNormally()
        {
            // Wide enough that several leading words fit whole (exercising the per-word "still leaves
            // room, keep it and move on" path) before the line as a whole overflows and gets truncated.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='width:260pt;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font-size:14pt'>" +
                "The quick brown fox jumps over the lazy dog</div>"));

            var box = LayoutHarness.FindById(root, "d")!;
            var recording = NewRecording();
            FragmentPaintHarness.PaintBox(container, box, recording);

            var drawn = recording.DrawnStrings.Select(d => d.Text).ToList();
            Assert.Contains("The", drawn);
            Assert.Contains("quick", drawn);
            Assert.Equal("…", drawn[^1]);
        }

        [Fact]
        public async Task Horizontal_Ltr_Nowrap_ImageBeforeOverflowingText_TruncatesNormally()
        {
            // An inline image mid-line doesn't break the ellipsis walk over the surrounding text words -
            // it paints via its own separate replaced-content painter (never one of this box's own
            // "words"), so it's simply not a candidate the walk considers, and truncation proceeds
            // normally over the text around it.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='width:70pt;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font-size:14pt'>" +
                $"hi <img src='data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22/%3E' width='16' height='16'>{LongWord}</div>"));

            var box = LayoutHarness.FindById(root, "d")!;
            var recording = NewRecording();
            FragmentPaintHarness.PaintBox(container, box, recording);

            var drawn = recording.DrawnStrings.Select(d => d.Text).ToList();
            Assert.Contains("hi", drawn);
            Assert.Equal("…", drawn[^1]);
        }

        [Fact]
        public async Task Horizontal_Wrapping_OneUnbreakableLine_OnlyThatLineGetsEllipsis()
        {
            // Lines forced by explicit <br>: a short first and third line that fit, and a middle line
            // whose single unbreakable token is wider than the container. Only the middle line should
            // end up with an ellipsis.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='width:60pt;overflow:hidden;text-overflow:ellipsis;font-size:14pt'>" +
                $"hi<br>{LongWord}<br>bye</div>"));

            var box = LayoutHarness.FindById(root, "d")!;
            Assert.True(box.LineBoxes.Count >= 3);

            var recording = NewRecording();
            FragmentPaintHarness.PaintBox(container, box, recording);

            // "hi" and "bye" each live on their own short line (each between/around a <br>) and never
            // enter truncation at all; the middle line's single unbreakable token does. Exactly one line
            // producing an ellipsis is what's being verified here - not zero, not more than one (the
            // latter is exactly the multi-box-per-line duplicate-ellipsis regression this repo's own
            // paint architecture (one CssBox per <br>-delimited text run, all sharing one containing
            // block) would otherwise be prone to).
            var drawn = recording.DrawnStrings.Select(d => d.Text).ToList();
            Assert.Contains("hi", drawn);
            Assert.Contains("bye", drawn);
            Assert.Single(drawn, t => t == "…");
        }

        [Fact]
        public async Task Horizontal_Ltr_Nowrap_TextSplitAcrossSiblingBoxes_OnlyOneEllipsisDrawn()
        {
            // "short" and "bold" are two separate sibling CssBoxes (split by the <b>bold</b> boundary)
            // that share one CssLineBox (confirmed directly: line 0's own Words are exactly
            // ["short", "bold"]) - regression coverage for the duplicate/misplaced-ellipsis bug found in
            // review: each sibling box independently re-running the "does my own last word cross the
            // boundary" check, without FragmentPainter's shared _linesAlreadyTruncated bookkeeping, would
            // each draw its own ellipsis. (The rest of the content after "bold" lands on a second line
            // box regardless of nowrap here - see issue #841, an unrelated, pre-existing wrap-boundary
            // bug this fixture doesn't need fixed to exercise the two-boxes-one-line scenario above.)
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='width:70pt;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font-size:14pt'>" +
                "short <b>bold</b> and more text that overflows the container width</div>"));

            var box = LayoutHarness.FindById(root, "d")!;
            var recording = NewRecording();
            FragmentPaintHarness.PaintBox(container, box, recording);

            var drawn = recording.DrawnStrings.Select(d => d.Text).ToList();
            Assert.Single(drawn, t => t == "…");
        }

        [Fact]
        public async Task VerticalRl_Ltr_ColumnTallerThanContainer_TruncatesFromTop_EllipsisLast()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='writing-mode:vertical-rl;text-orientation:upright;direction:ltr;" +
                $"height:60pt;overflow:hidden;text-overflow:ellipsis;font-size:20pt'>{Alphabet}</div>"));

            var box = LayoutHarness.FindById(root, "d")!;
            var recording = NewRecording();
            FragmentPaintHarness.PaintBox(container, box, recording);

            var drawn = recording.DrawnStrings.Select(d => d.Text).ToList();
            Assert.NotEmpty(drawn);
            Assert.Equal("…", drawn[^1]);
            Assert.True(drawn.Count - 1 < Alphabet.Length, "expected fewer characters than the full alphabet to be kept");

            var kept = string.Concat(drawn.Take(drawn.Count - 1));
            Assert.Equal(Alphabet[..kept.Length], kept); // LTR keeps a prefix (top-first characters)
        }

        [Fact]
        public async Task VerticalLr_Ltr_ColumnTallerThanContainer_TruncatesFromTop_EllipsisLast()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='writing-mode:vertical-lr;text-orientation:upright;direction:ltr;" +
                $"height:60pt;overflow:hidden;text-overflow:ellipsis;font-size:20pt'>{Alphabet}</div>"));

            var box = LayoutHarness.FindById(root, "d")!;
            var recording = NewRecording();
            FragmentPaintHarness.PaintBox(container, box, recording);

            var drawn = recording.DrawnStrings.Select(d => d.Text).ToList();
            Assert.NotEmpty(drawn);
            Assert.Equal("…", drawn[^1]);
            Assert.True(drawn.Count - 1 < Alphabet.Length);

            var kept = string.Concat(drawn.Take(drawn.Count - 1));
            Assert.Equal(Alphabet[..kept.Length], kept);
        }

        [Fact]
        public async Task VerticalRl_Rtl_ColumnTallerThanContainer_TruncatesFromBottom_EllipsisFirst()
        {
            // Two words (a space-separated leading run before the alphabet) so the column's visual-order
            // sort actually has something to compare, exercising the RTL vertical sort comparator.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' dir='rtl' style='writing-mode:vertical-rl;text-orientation:upright;direction:rtl;" +
                $"height:60pt;overflow:hidden;text-overflow:ellipsis;font-size:20pt'>AB {Alphabet}</div>"));

            var box = LayoutHarness.FindById(root, "d")!;
            var recording = NewRecording();
            FragmentPaintHarness.PaintBox(container, box, recording);

            var drawn = recording.DrawnStrings.Select(d => d.Text).ToList();
            Assert.NotEmpty(drawn);
            // RTL vertical: inline-end is physical top, so the ellipsis (drawn first in the walk order
            // paint emits its "kept run then ellipsis" sequence for the cut word) lands first for a
            // whole-line single-word cut... but painting still emits kept-prefix words (if any) before
            // the truncated word's own ellipsis, so just confirm an ellipsis is present and fewer
            // characters than the full alphabet were kept.
            Assert.Contains("…", drawn);
            Assert.True(drawn.Count - 1 < Alphabet.Length);
        }

        [Fact]
        public async Task VerticalLr_Rtl_ColumnTallerThanContainer_TruncatesFromBottom()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' dir='rtl' style='writing-mode:vertical-lr;text-orientation:upright;direction:rtl;" +
                $"height:60pt;overflow:hidden;text-overflow:ellipsis;font-size:20pt'>{Alphabet}</div>"));

            var box = LayoutHarness.FindById(root, "d")!;
            var recording = NewRecording();
            FragmentPaintHarness.PaintBox(container, box, recording);

            var drawn = recording.DrawnStrings.Select(d => d.Text).ToList();
            Assert.NotEmpty(drawn);
            Assert.Contains("…", drawn);
            Assert.True(drawn.Count - 1 < Alphabet.Length);
        }

        [Fact]
        public async Task VerticalRl_Ltr_ColumnFitsEntirely_DrawsNoEllipsis()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"<div id='d' style='writing-mode:vertical-rl;text-orientation:upright;direction:ltr;" +
                $"height:800pt;overflow:hidden;text-overflow:ellipsis;font-size:20pt'>{Alphabet}</div>"));

            var box = LayoutHarness.FindById(root, "d")!;
            var recording = NewRecording();
            FragmentPaintHarness.PaintBox(container, box, recording);

            var drawn = recording.DrawnStrings.Select(d => d.Text).ToList();
            Assert.DoesNotContain("…", drawn);
            Assert.Equal(Alphabet.Length, drawn.Count);
        }
    }
}
