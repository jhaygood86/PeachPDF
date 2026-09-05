using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end coverage that a real bidi-mirrored word actually reaches
    /// <see cref="RGraphics.DrawString(string, PeachPDF.Html.Adapters.RFont, PeachPDF.Html.Adapters.Entities.RColor, PeachPDF.Html.Adapters.Entities.RPoint, PeachPDF.Html.Adapters.Entities.RSize, double, PeachPDF.Html.Adapters.Entities.RFontPalette?, PeachPDF.Text.TextShapingFeatures?, string?)"/>'s
    /// <c>logicalText</c> parameter with its true logical-order source - the plumbing half of the
    /// <see cref="PeachPDF.Fonts.CMapInfo.AddShapedText"/> ToUnicode fix (see
    /// <c>CMapInfoLogicalTextTests</c> for direct coverage of the remap math itself). Without this
    /// wiring, <see cref="PeachPDF.Html.Core.Paint.FragmentPainter"/> would still paint the correct
    /// (mirrored) glyphs on the page - only text extraction from the resulting PDF would be wrong -
    /// which is exactly why the original defect went unnoticed until real PDFium/MuPDF extraction was
    /// checked directly.
    /// </summary>
    public class RtlToUnicodeIntegrationTests
    {
        [Fact]
        public async Task BdoReversedWordWithMirroredPunctuation_PaintsWithItsTrueLogicalSource()
        {
            // <bdo dir="rtl"> forces every character (including the parens, ordinarily bidi-neutral) to
            // resolve as one right-to-left run (see CssLayoutEngineBidiTests.Bdo_DirRtl_ReversesPlainLatinTextInLayout),
            // so this word's own Text is mirrored in place before painting: reverse("(AB)") + mirror
            // each character = "(BA)" - the parens swap identity, not just position, exactly the
            // real-world extraction defect (a parenthesized RTL word) this fix targets.
            var html = LayoutHarness.Wrap("""<p><bdo id="b" dir="rtl">(AB)</bdo></p>""");
            var (root, container) = await LayoutHarness.LayoutAsync(html);

            var bdoElement = LayoutHarness.FindById(root, "b");
            Assert.NotNull(bdoElement);
            var bdo = FindWordsBox(bdoElement!);
            var word = Assert.Single(bdo.Words);

            Assert.Equal("(BA)", word.Text);

            var recorder = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, bdo, recorder);

            var op = Assert.Single(recorder.Log, o => o.Kind == PaintOpKind.DrawString);
            Assert.Equal("(BA)", op.Text);
            // logicalText must reach DrawString positionally aligned with the painted (visual) string,
            // not simply as the original "(AB)" - ')' stands at the painted '(' position (the character
            // whose mirror image is painted there), and vice versa for the trailing ')' -> '('.
            Assert.Equal(")BA(", op.LogicalText);
        }

        [Fact]
        public async Task UprightVerticalRtlWordWithMirroredPunctuation_PaintsEachCharacterWithItsTrueLogicalSource()
        {
            // text-orientation: upright forces every word's isUpright branch regardless of each
            // character's own Vertical_Orientation classification (CssLayoutEngine's own "upright/
            // sideways force one answer for every word" remark), so this exercises
            // PaintUprightVerticalRun's own per-character logicalText handling - distinct from the
            // sideways-rotated branch the other RTL test above already covers, since upright text paints
            // one character per DrawString call rather than the whole word as a single call. <bdo dir="rtl">
            // (not plain dir="rtl") keeps this as one single word: a plain dir="rtl" paragraph would
            // resolve the parens' neutral bidi type independently from the strong-L "AB" run and split
            // into three separate words at the resulting embedding-level boundary (the same mechanism
            // CssLayoutEngineBidiTests.RtlParagraph_DigitRunWithNoSurroundingWhitespace_SplitsIntoItsOwnWord
            // covers) - isolate-override forces every character, parens included, to one uniform level.
            var html = LayoutHarness.Wrap(
                """<p style="writing-mode:vertical-rl; text-orientation:upright; height:400px"><bdo id="b" dir="rtl">(AB)</bdo></p>""");
            var (root, container) = await LayoutHarness.LayoutAsync(html);

            var bdoElement = LayoutHarness.FindById(root, "b");
            Assert.NotNull(bdoElement);
            var p = FindWordsBox(bdoElement!);
            var word = Assert.Single(p.Words);

            Assert.Equal("(BA)", word.Text);

            var recorder = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, p, recorder);

            // One DrawString call per character: '(' 'B' 'A' ')' (the painted/visual order), each
            // needing its own true logical-order source recovered - '(' stands in for the logical ')',
            // 'B'/'A' just moved position, ')' stands in for the logical '('.
            var drawStringOps = recorder.Log.Where(o => o.Kind == PaintOpKind.DrawString).ToList();
            Assert.Equal(4, drawStringOps.Count);
            Assert.Equal([("(", ")"), ("B", "B"), ("A", "A"), (")", "(")],
                drawStringOps.Select(o => (o.Text, o.LogicalText)));
        }

        [Fact]
        public async Task PlainLtrWord_PaintsWithNoLogicalTextOverride()
        {
            // The overwhelming common case: a word that was never mirrored has nothing to recover, and
            // must not pay for this feature's existence - confirms the fast/no-op path stays a no-op.
            var html = LayoutHarness.Wrap("""<p id="p">hello</p>""");
            var (root, container) = await LayoutHarness.LayoutAsync(html);

            var pElement = LayoutHarness.FindById(root, "p");
            Assert.NotNull(pElement);
            var p = FindWordsBox(pElement!);

            var recorder = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, p, recorder);

            var op = Assert.Single(recorder.Log, o => o.Kind == PaintOpKind.DrawString);
            Assert.Equal("hello", op.Text);
            // A never-mirrored word has nothing distinct to recover - the call site itself detects
            // PreMirrorText == Text and passes null, rather than reversing it needlessly.
            Assert.Null(op.LogicalText);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The box that actually owns <paramref name="element"/>'s words - itself if it holds them
        /// directly (an ordinary inline run), or its anonymous inline-content wrapper child otherwise
        /// (a block element's own direct text, or - as with a bidi-isolating &lt;bdo&gt; - an inline
        /// element whose isolation still gets wrapped). Mirrors
        /// <c>FontVariantLigaturesIntegrationTests.FindWordsBox</c>.
        /// </summary>
        private static CssBox FindWordsBox(CssBox element)
        {
            if (element.Words.Count > 0) return element;

            var wordsChild = element.Boxes.FirstOrDefault(b => b.Words.Count > 0);
            Assert.NotNull(wordsChild);
            return wordsChild!;
        }
    }
}
