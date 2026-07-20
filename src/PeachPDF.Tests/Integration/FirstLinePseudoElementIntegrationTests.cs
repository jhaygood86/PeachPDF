using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Integration coverage for the CSS2.1 <c>::first-line</c> pseudo-element, restricted (per spec) to
    /// font/color/background/text-decoration/word-spacing/letter-spacing/vertical-align/text-transform -
    /// no box-model properties. Covers all 7 properties PeachPDF implements, plus the structural cases
    /// (non-width-affecting vs width-affecting, single-line, nested-inline, straddling-box full-fidelity,
    /// and the disallowed-property no-op guard).
    /// </summary>
    public class FirstLinePseudoElementIntegrationTests
    {
        [Fact]
        public async Task Color_AppliesToFirstLineOnly()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>p::first-line { color: rgb(255,0,0) }</style>" +
                "<p id='p' style='width:120px'>Some fairly long wrapped text here today</p>"));
            var p = FindById(root, "p")!;

            var g = new TestRecordingGraphics();
            await p.Paint(g);

            var calls = g.DrawStringCalls;
            Assert.True(calls.Count > 1, $"expected multiple draw calls, got {calls.Count}");

            var firstLineTop = p.LineBoxes[0].Rectangles.Values.Min(r => r.Top);
            var firstLineBottom = p.LineBoxes[0].LineBottom;

            var onFirstLine = calls.Where(c => c.Point.Y >= firstLineTop && c.Point.Y < firstLineBottom).ToList();
            var onLaterLines = calls.Where(c => c.Point.Y >= firstLineBottom).ToList();

            Assert.NotEmpty(onFirstLine);
            Assert.NotEmpty(onLaterLines);
            Assert.All(onFirstLine, c => Assert.Equal(RColor.FromArgb(255, 0, 0), c.Color));
            Assert.All(onLaterLines, c => Assert.NotEqual(RColor.FromArgb(255, 0, 0), c.Color));
        }

        [Fact]
        public async Task FontSize_WidthAffecting_ChangesWrapPoint()
        {
            var withRule = await BuildAndLayout(Wrap(
                "<style>p::first-line { font-size: 250% }</style>" +
                "<p id='p' style='width:150px; font-size:10pt'>one two three four five six seven eight</p>"));
            var without = await BuildAndLayout(Wrap(
                "<p id='p' style='width:150px; font-size:10pt'>one two three four five six seven eight</p>"));

            var pWith = FindById(withRule.root, "p")!;
            var pWithout = FindById(without.root, "p")!;

            var wordsOnLine1With = pWith.LineBoxes[0].Words.Count;
            var wordsOnLine1Without = pWithout.LineBoxes[0].Words.Count;

            Assert.True(wordsOnLine1With < wordsOnLine1Without,
                $"expected fewer words on line 1 with bigger first-line font: with={wordsOnLine1With} without={wordsOnLine1Without}");
        }

        [Fact]
        public async Task WordSpacing_WidthAffecting_ChangesWrapPoint()
        {
            var withRule = await BuildAndLayout(Wrap(
                "<style>p::first-line { word-spacing: 40px }</style>" +
                "<p id='p' style='width:150px; font-size:10pt'>one two three four five six seven eight</p>"));
            var without = await BuildAndLayout(Wrap(
                "<p id='p' style='width:150px; font-size:10pt'>one two three four five six seven eight</p>"));

            var pWith = FindById(withRule.root, "p")!;
            var pWithout = FindById(without.root, "p")!;

            var wordsOnLine1With = pWith.LineBoxes[0].Words.Count;
            var wordsOnLine1Without = pWithout.LineBoxes[0].Words.Count;

            Assert.True(wordsOnLine1With < wordsOnLine1Without,
                $"expected fewer words on line 1 with wide first-line word-spacing: with={wordsOnLine1With} without={wordsOnLine1Without}");
        }

        [Fact]
        public async Task LetterSpacing_WidthAffecting_ChangesWrapPoint()
        {
            var withRule = await BuildAndLayout(Wrap(
                "<style>p::first-line { letter-spacing: 6px }</style>" +
                "<p id='p' style='width:150px; font-size:10pt'>one two three four five six seven eight</p>"));
            var without = await BuildAndLayout(Wrap(
                "<p id='p' style='width:150px; font-size:10pt'>one two three four five six seven eight</p>"));

            var pWith = FindById(withRule.root, "p")!;
            var pWithout = FindById(without.root, "p")!;

            var wordsOnLine1With = pWith.LineBoxes[0].Words.Count;
            var wordsOnLine1Without = pWithout.LineBoxes[0].Words.Count;

            Assert.True(wordsOnLine1With < wordsOnLine1Without,
                $"expected fewer words on line 1 with wide first-line letter-spacing: with={wordsOnLine1With} without={wordsOnLine1Without}");
        }

        [Fact]
        public async Task StraddlingBox_TailWordsRevertToNormalFontAndWidth()
        {
            // A single <b> spans the line-1/2 boundary under a big first-line font-size - the full-
            // fidelity requirement: words that land on line 1 must use the first-line font/width, but
            // words that overflow to line 2 must revert to the box's own normal font/width, not stay
            // stuck with the first-line one.
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>p::first-line { font-size: 300% }</style>" +
                "<p id='p' style='width:80pt; font-size:10pt'><b id='b'>alpha beta gamma delta epsilon</b></p>"));
            var p = FindById(root, "p")!;
            var b = FindById(root, "b")!;

            Assert.True(p.LineBoxes.Count > 1, "expected content to wrap across at least 2 lines");

            // <b> doesn't own its text words directly - the HTML text node is its own anonymous child
            // box - so gather words by walking b's whole subtree rather than reading b.Words.
            var allWordsUnderB = AllDescendants(b).Prepend(b).SelectMany(x => x.Words).ToList();
            var line1WordSet = p.LineBoxes[0].Words.ToHashSet();

            var line1Words = allWordsUnderB.Where(w => line1WordSet.Contains(w)).ToList();
            var laterWords = allWordsUnderB.Except(line1Words).ToList();

            Assert.NotEmpty(line1Words);
            Assert.NotEmpty(laterWords);

            Assert.All(line1Words, w => Assert.NotNull(w.FirstLineStyle));
            Assert.All(laterWords, w => Assert.Null(w.FirstLineStyle));

            // Later words' widths must reflect the box's own (10pt) font, not the first-line (300%,
            // i.e. 30pt) one that was used to measure them before the boundary-crossing correction ran -
            // per-character width should be roughly 3x smaller than line-1 words' once reverted.
            var line1PerChar = line1Words.Where(w => !w.IsImage && w.Text != "\n")
                .Select(w => w.Width / w.Text!.Length).Average();
            var laterPerChar = laterWords.Where(w => !w.IsImage && w.Text != "\n")
                .Select(w => w.Width / w.Text!.Length).Average();

            Assert.True(laterPerChar < line1PerChar / 2,
                $"expected reverted words' per-char width ({laterPerChar}) to be much smaller than first-line words' ({line1PerChar})");
        }

        [Fact]
        public async Task StraddlingBox_RevertedTailWord_ReservesLetterSpacingAfterEveryCharacter()
        {
            // RemeasureWordsTail (CssBox.cs) re-measures a word that turns out to wrap past line 1 using
            // the box's own normal style, not the first-line one it was originally measured against - it
            // must reserve N letter-spacing gaps for an N-character word (matching the PDF Tc operator's
            // actual per-glyph behavior), not the old N-1 model, the same as ordinary (non-reverted)
            // measurement. Compared here against a plain, non-boundary-crossing paragraph carrying the
            // identical letter-spacing and text, which is unambiguously covered by the ordinary
            // MeasureWordsSize path.
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>#p::first-line { font-size: 300% }</style>" +
                "<p id='p' style='width:80px; font-size:10pt; letter-spacing:2px'>" +
                "<b id='b'>alpha beta gamma delta epsilon</b></p>" +
                "<div id='ref' style='font-size:10pt; letter-spacing:2px'><b>beta</b></div>"));
            var p = FindById(root, "p")!;
            var b = FindById(root, "b")!;
            var reference = FindById(root, "ref")!;

            Assert.True(p.LineBoxes.Count > 1, "expected content to wrap across at least 2 lines");

            var allWordsUnderB = AllDescendants(b).Prepend(b).SelectMany(x => x.Words).ToList();
            // RemeasureWordsTail explicitly nulls out FirstLineStyle for a word it reverts (see its own
            // "boxWord.FirstLineStyle = null;") - the direct, reliable signal that this specific word's
            // width was recomputed by that method rather than the ordinary MeasureWordsSize path.
            var revertedBeta = allWordsUnderB.FirstOrDefault(w => w.FirstLineStyle == null && w.Text == "beta");
            var referenceBeta = CssBox.FirstWordOccurence(reference, reference.LineBoxes[0]);

            Assert.NotNull(revertedBeta);
            Assert.NotNull(referenceBeta);
            Assert.Equal(referenceBeta!.Width, revertedBeta!.Width, 1);
        }

        [Fact]
        public async Task SingleLine_NoWrap_WholeContentQualifiesAsFirstLine()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>p::first-line { color: rgb(0,0,255) }</style>" +
                "<p id='p' style='width:500px'>short line of text</p>"));
            var p = FindById(root, "p")!;

            Assert.Single(p.LineBoxes);

            var allWords = AllDescendants(p).Prepend(p).SelectMany(x => x.Words).ToList();
            Assert.NotEmpty(allWords);
            Assert.All(allWords.Where(w => !w.IsImage), w => Assert.NotNull(w.FirstLineStyle));

            var g = new TestRecordingGraphics();
            await p.Paint(g);
            Assert.NotEmpty(g.DrawStringCalls);
            Assert.All(g.DrawStringCalls, c => Assert.Equal(RColor.FromArgb(0, 0, 255), c.Color));
        }

        [Fact]
        public async Task NestedInline_AppliesThroughMultipleLevelsWithoutBoxSplitting()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>p::first-line { color: rgb(0,128,0) }</style>" +
                "<p id='p' style='width:500px'>plain <em><strong id='deep'>deeply nested</strong></em> text</p>"));
            var p = FindById(root, "p")!;
            var deep = FindById(root, "deep")!;

            Assert.Single(p.LineBoxes);

            var g = new TestRecordingGraphics();
            await p.Paint(g);

            Assert.NotEmpty(g.DrawStringCalls);
            Assert.All(g.DrawStringCalls, c => Assert.Equal(RColor.FromArgb(0, 128, 0), c.Color));

            var deepWords = AllDescendants(deep).Prepend(deep).SelectMany(x => x.Words).ToList();
            Assert.NotEmpty(deepWords);
            Assert.All(deepWords, w => Assert.NotNull(w.FirstLineStyle));
        }

        [Fact]
        public async Task TextDecoration_AppliesToFirstLineOnly()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>p::first-line { text-decoration: underline }</style>" +
                "<p id='p' style='width:120px'>Some fairly long wrapped text here today</p>"));
            var p = FindById(root, "p")!;

            var g = new TestRecordingGraphics();
            await p.Paint(g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            Assert.NotEmpty(lines);

            var firstLineBottom = p.LineBoxes[0].LineBottom;
            Assert.Contains(lines, l => l.Y1 < firstLineBottom);
            Assert.DoesNotContain(lines, l => l.Y1 >= firstLineBottom);
        }

        [Fact]
        public async Task BackgroundColor_AppliesToFirstLineOnly()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>p::first-line { background-color: rgb(0,255,0) }</style>" +
                "<p id='p' style='width:120px'>Some fairly long wrapped text here today</p>"));
            var p = FindById(root, "p")!;

            var g = new TestRecordingGraphics();
            await p.Paint(g);

            var rects = g.Log.OfType<TestRecordingGraphics.DrawRectCall>().ToList();
            Assert.Contains(rects, r => r.Color == RColor.FromArgb(0, 255, 0));
        }

        [Fact]
        public async Task VerticalAlign_OverridesInlineBoxPositionOnFirstLineOnly()
        {
            var withRule = await BuildAndLayout(Wrap(
                "<style>p::first-line { vertical-align: super }</style>" +
                "<p id='p' style='width:300px'>before <span id='s'>MID</span> after</p>"));
            var without = await BuildAndLayout(Wrap(
                "<p id='p' style='width:300px'>before <span id='s'>MID</span> after</p>"));

            var sWith = FindById(withRule.root, "s")!;
            var sWithout = FindById(without.root, "s")!;

            // A same-size inline box's own Rectangles entry is only updated by SetBaseLine when its
            // rect is shorter than its parent's on this line (see CssLineBox.SetBaseLine) - here "s" is
            // the same font size as "p", so the observable effect of vertical-align is on its words'
            // Top, not its own Rectangles entry.
            var wordTopWith = AllDescendants(sWith).Prepend(sWith).SelectMany(x => x.Words).First().Top;
            var wordTopWithout = AllDescendants(sWithout).Prepend(sWithout).SelectMany(x => x.Words).First().Top;

            Assert.NotEqual(wordTopWithout, wordTopWith);
        }

        [Fact]
        public async Task VerticalAlign_DoesNotOverrideOwnExplicitValueWhenRuleOmitsIt()
        {
            // Regression guard: VerticalAlign is unconditionally copied by InheritStyle's "always"
            // section, so the ::first-line shadow box always reports a non-null VerticalAlign matching
            // the block's own - a rule that never declares vertical-align (only color, here) must not
            // spuriously override an inline element's own explicit vertical-align.
            var withRule = await BuildAndLayout(Wrap(
                "<style>p::first-line { color: rgb(255,0,0) }</style>" +
                "<p id='p' style='width:300px'>before <span id='s' style='vertical-align:sub'>MID</span> after</p>"));
            var without = await BuildAndLayout(Wrap(
                "<p id='p' style='width:300px'>before <span id='s' style='vertical-align:sub'>MID</span> after</p>"));

            var sWith = FindById(withRule.root, "s")!;
            var sWithout = FindById(without.root, "s")!;

            var wordTopWith = AllDescendants(sWith).Prepend(sWith).SelectMany(x => x.Words).First().Top;
            var wordTopWithout = AllDescendants(sWithout).Prepend(sWithout).SelectMany(x => x.Words).First().Top;

            Assert.Equal(wordTopWithout, wordTopWith);
        }

        [Fact]
        public async Task TextTransform_UppercasesFirstLineOnly()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>p::first-line { text-transform: uppercase }</style>" +
                "<p id='p' style='width:120px'>some fairly long wrapped text here today</p>"));
            var p = FindById(root, "p")!;

            var allWords = AllDescendants(p).Prepend(p).SelectMany(x => x.Words).ToList();
            var line1WordSet = p.LineBoxes[0].Words.ToHashSet();
            var line1Words = allWords.Where(w => line1WordSet.Contains(w)).ToList();
            var laterWords = allWords.Except(line1Words).ToList();

            Assert.NotEmpty(line1Words);
            Assert.NotEmpty(laterWords);

            Assert.All(line1Words, w => Assert.Equal((w.FirstLineText ?? w.Text)!.ToUpperInvariant(), w.FirstLineText ?? w.Text));
            Assert.All(line1Words, w => Assert.NotNull(w.FirstLineText));
            Assert.All(laterWords, w => Assert.Null(w.FirstLineText));

            var g = new TestRecordingGraphics();
            await p.Paint(g);

            var calls = g.DrawStringCalls;
            var firstLineBottom = p.LineBoxes[0].LineBottom;
            var onFirstLine = calls.Where(c => c.Point.Y < firstLineBottom).ToList();
            var onLaterLines = calls.Where(c => c.Point.Y >= firstLineBottom).ToList();

            Assert.NotEmpty(onFirstLine);
            Assert.NotEmpty(onLaterLines);
            Assert.All(onFirstLine, c => Assert.Equal(c.Text.ToUpperInvariant(), c.Text));
        }

        [Fact]
        public async Task TextTransform_CapitalizeSurvivesBoxOwnUppercaseTransform()
        {
            // The reason OriginalText (pre-transform source) must be tracked per word, not derived by
            // re-transforming the box's own already-transformed Text: if the box's own text-transform is
            // uppercase, all casing information needed to correctly apply the first-line rule's own
            // capitalize is otherwise destroyed - re-deriving from an already-uppercased "HELLO" can only
            // ever produce "Hello" by coincidence, never a real capitalize of mixed-case source text.
            var (root, _) = await BuildAndLayout(Wrap(
                "<style>p::first-line { text-transform: capitalize }</style>" +
                "<p id='p' style='width:500px; text-transform:uppercase'>hello world</p>"));
            var p = FindById(root, "p")!;

            Assert.Single(p.LineBoxes);

            var allWords = AllDescendants(p).Prepend(p).SelectMany(x => x.Words).Where(w => !w.IsImage).ToList();
            var texts = allWords.Select(w => w.FirstLineText).ToList();

            Assert.Contains("Hello", texts);
            Assert.Contains("World", texts);
        }

        [Fact]
        public async Task DisallowedProperty_MarginHasNoEffect()
        {
            var withRule = await BuildAndLayout(Wrap(
                "<style>p::first-line { margin: 50px; padding: 30px }</style>" +
                "<p id='p' style='width:200px'>some text here</p>"));
            var without = await BuildAndLayout(Wrap(
                "<p id='p' style='width:200px'>some text here</p>"));

            var pWith = FindById(withRule.root, "p")!;
            var pWithout = FindById(without.root, "p")!;

            Assert.Equal(pWithout.MarginTop, pWith.MarginTop);
            Assert.Equal(pWithout.Bounds.Location, pWith.Bounds.Location);
            Assert.Equal(pWithout.Bounds.Size, pWith.Bounds.Size);
        }

        private static IEnumerable<CssBox> AllDescendants(CssBox box)
        {
            foreach (var child in box.Boxes)
            {
                yield return child;
                foreach (var grandchild in AllDescendants(child))
                    yield return grandchild;
            }
        }

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }
    }
}
