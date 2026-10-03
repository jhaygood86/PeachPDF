using PeachDrawing.Core;
using PeachDrawing.Text.Shaping;
using PeachPDF.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A rendered word separator is shown as a real space glyph, not only as the gap between two
    /// independently drawn words. Without it every reader that takes the text in content-stream order
    /// (plain full-text indexers, copy and paste in viewers that do not reconstruct gaps) runs the words
    /// together - <c>Premium Widget</c> extracted as <c>PremiumWidget</c>. ISO 32000-1 §14.8.2.5 asks for
    /// word breaks to be represented by an actual space character.
    /// </summary>
    public class WordSeparatorGlyphTests
    {
        private const double Tolerance = 0.01;

        [Fact]
        public async Task SpaceBetweenTwoWords_IsPaintedAsAGlyphInTheGap()
        {
            var ops = await PaintTextAsync("""<p>Premium Widget</p>""");

            Assert.Equal(["Premium", " ", "Widget"], ops.Select(o => o.Text));
            AssertBetween(ops[1].Bounds.Left, ops[1].Bounds.Right, ops[0].Bounds.Right, ops[2].Bounds.Left);
        }

        [Fact]
        public async Task WhiteSpaceOnlyInlineBoxBetweenTwoSpans_IsPaintedAsASpace()
        {
            // The space here belongs to neither word - the flow adds it for the anonymous box between the
            // two spans - so a check keyed on HasSpaceAfter/HasSpaceBefore alone would miss it.
            var ops = await PaintTextAsync("""<p><span>AA</span> <span>BB</span></p>""");

            Assert.Equal("AA BB", string.Concat(ops.Select(o => o.Text)));
        }

        [Fact]
        public async Task AdjacentSpansWithoutWhiteSpace_GetNoSpace()
        {
            var ops = await PaintTextAsync("""<p><span>AA</span><span>BB</span></p>""");

            Assert.Equal("AABB", string.Concat(ops.Select(o => o.Text)));
        }

        [Fact]
        public async Task WordOpeningAWrappedLine_GetsNoLeadingSpace()
        {
            // css-text-3 phase II removes the space at the start of a line, so nothing is shown there.
            var ops = await PaintTextAsync("""<p style="width:40pt">Premium Widget</p>""");

            Assert.Equal(["Premium", "Widget"], ops.Select(o => o.Text));
            Assert.True(ops[1].Bounds.Top > ops[0].Bounds.Top, "the second word must have wrapped");
        }

        [Theory]
        [InlineData("mixed")]
        [InlineData("upright")]
        public async Task VerticalWordOpeningAWrappedColumn_GetsNoLeadingSpace(string orientation)
        {
            var ops = await PaintTextAsync(
                $"""<p style="writing-mode:vertical-rl; text-orientation:{orientation}; height:40pt">Premium Widget</p>""");

            Assert.DoesNotContain(ops, o => o.Text == " ");
        }

        [Fact]
        public async Task PreservedWhiteSpace_IsNotDoubled()
        {
            // Preserved white space is already painted as its own run; a second, synthesized space next
            // to it would read back as two.
            var ops = await PaintTextAsync("""<p style="white-space:pre">AA BB</p>""");

            Assert.Equal("AA BB", string.Concat(ops.Select(o => o.Text)));
        }

        [Fact]
        public async Task CollapsibleSpaceBeforePreservedSpaces_ShowsAllThree()
        {
            // The collapsible space after "AA" is not next to another collapsible one, so it stays
            // (css-text-3 §4.1.1) and the line holds three spaces: the separator plus the two preserved.
            var ops = await PaintTextAsync(
                """<p><span>AA </span><span style="white-space:pre">  BB</span></p>""");

            Assert.Equal("AA   BB", string.Concat(ops.Select(o => o.Text)));
        }

        [Fact]
        public async Task JustifiedLine_KeepsTheSpaceInsideTheWidenedGap()
        {
            var ops = await PaintTextAsync(
                """<p style="width:200pt; text-align:justify; text-align-last:justify">AA BB</p>""");

            Assert.Equal(["AA", " ", "BB"], ops.Select(o => o.Text));
            AssertBetween(ops[1].Bounds.Left, ops[1].Bounds.Right, ops[0].Bounds.Right, ops[2].Bounds.Left);
            // Flush against the word before it, with the widening after it - where a browser's text run
            // has it. In front of the following word instead, the widening sat ahead of the space and
            // extractors that rebuild a space from a wide gap read two.
            Assert.Equal(ops[0].Bounds.Right, ops[1].Bounds.Left, 3);
            Assert.True(ops[2].Bounds.Left - ops[1].Bounds.Right > 50, "the line must actually be widened");
        }

        [Fact]
        public async Task WordSpacing_GoesAfterTheSpace()
        {
            var ops = await PaintTextAsync("""<p style="word-spacing:20pt">AA BB</p>""");

            Assert.Equal(ops[0].Bounds.Right, ops[1].Bounds.Left, 3);
        }

        [Fact]
        public async Task SpaceAfterAnInlineImage_StartsAtTheImagesEdge()
        {
            // The image is the word the space after it follows, even though its content painter draws
            // it. (An image gets no space in front of it: only a text word paints one.)
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"""<p id="p">AA <img src="{OnePixelPng}" style="width:20pt;height:10pt"> BB</p>"""));
            using var recorder = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintPage(container, recorder);
            var image = AllWords(LayoutHarness.FindById(root, "p")!).Single(w => w.IsImage);

            var ops = recorder.Log.Where(o => o.Kind == PaintOpKind.DrawString).ToList();
            Assert.Equal(["AA", " ", "BB"], ops.Select(o => o.Text));
            Assert.Equal(image.Right, ops[1].Bounds.Left, 3);
        }

        [Fact]
        public async Task SpaceAfterAHiddenWord_FallsBackToTheWordItPrecedes()
        {
            // "b" is laid out but not drawn, so the last word drawn is not the one the space after it
            // follows: the space is anchored to "c" instead, still inside the gap.
            var ops = await PaintTextAsync("""<p>a <span style="visibility:hidden">b</span> c</p>""");

            Assert.Equal(["a", " ", "c"], ops.Select(o => o.Text));
            Assert.Equal(ops[2].Bounds.Left, ops[1].Bounds.Right, 3);
        }

        [Fact]
        public async Task SpaceInFrontOfAClippedInlineBlocksFirstWord_StaysInsideTheClip()
        {
            // The separator belongs to the outer line's gap, in front of the box's border, but it is
            // drawn under the box's own overflow clip; anchored to "before" it would be clipped away.
            var ops = await PaintAllAsync(
                """<p>before <span style="display:inline-block; overflow:hidden; padding:0 4pt">inside</span></p>""");

            var texts = ops.Where(o => o.Kind == PaintOpKind.DrawString).ToList();
            Assert.Equal(["before", " ", "inside"], texts.Select(o => o.Text));
            Assert.Equal(texts[2].Bounds.Left, texts[1].Bounds.Right, 3);
        }

        [Fact]
        public async Task SpaceBeforeATranslucentWord_IsStillShown()
        {
            var ops = await PaintTextAsync("""<p>a <span style="opacity:0.5">b</span></p>""");

            Assert.Equal("a b", string.Concat(ops.Select(o => o.Text)));
        }

        [Fact]
        public async Task SpaceTakesTheColourAndLetterSpacingOfTheBoxThatMeasuredIt()
        {
            // Neither shows on an inkless glyph; matching the word before it keeps the PDF writer from
            // switching fill colour and character spacing just for the space.
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                """<p><span style="color:red; letter-spacing:2pt">a </span><span style="color:blue">b</span></p>"""));
            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var calls = g.DrawStringCalls;
            Assert.Equal(["a", " ", "b"], calls.Select(c => c.Text));
            Assert.Equal(calls[0].PaintColor, calls[1].PaintColor);
            Assert.NotEqual(calls[2].PaintColor, calls[1].PaintColor);
            Assert.Equal(calls[0].LetterSpacing, calls[1].LetterSpacing);
            Assert.True(calls[1].LetterSpacing > 0);
        }

        [Fact]
        public async Task SpaceOnAFirstLine_IsDrawnInTheFirstLineFont()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                """<!DOCTYPE html><html><head><style>p::first-line { font-size: 30pt }</style></head><body style="margin:0"><p style="font-size:10pt">aa bb</p></body></html>""");
            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var calls = g.DrawStringCalls;
            Assert.Equal(["aa", " ", "bb"], calls.Select(c => c.Text));
            Assert.Equal(calls[0].Font.Size, calls[1].Font.Size, 3);
            Assert.True(calls[1].Font.Size > 20, "the ::first-line font must apply");
        }

        [Fact]
        public async Task RightToLeftWords_PutTheSpaceBetweenThemOnTheLogicallyPrecedingSide()
        {
            // <bdo dir="rtl"> resolves both words right-to-left: logical "AB" is painted (mirrored) on the
            // right, "CD" on the left. The separator belongs between them, on the side of the word that
            // logically precedes - the right edge of "DC".
            var ops = await PaintTextAsync("""<p><bdo dir="rtl">AB CD</bdo></p>""");

            Assert.Equal(["BA", " ", "DC"], ops.Select(o => o.Text));
            var right = ops[0].Bounds;
            var left = ops[2].Bounds;
            Assert.True(left.Right <= right.Left, "the logically second word must sit to the left");
            AssertBetween(ops[1].Bounds.Left, ops[1].Bounds.Right, left.Right, right.Left);
        }

        [Fact]
        public async Task JustifiedRightToLeftWords_PutTheSpaceAgainstThePrecedingWordsLeftEdge()
        {
            // In a widened right-to-left gap the space sits flush against the word it follows - on that
            // word's left - with the widening after it, on the far side.
            var ops = await PaintTextAsync(
                """<p style="width:200pt; text-align:justify; text-align-last:justify"><bdo dir="rtl">AB CD</bdo></p>""");

            Assert.Equal(["BA", " ", "DC"], ops.Select(o => o.Text));
            Assert.Equal(ops[0].Bounds.Left, ops[1].Bounds.Right, 3);
            Assert.True(ops[1].Bounds.Left - ops[2].Bounds.Right > 50, "the line must actually be widened");
        }

        [Fact]
        public async Task SpaceAtADirectionChange_IsNotAnchoredToTheWordOnTheOtherSide()
        {
            // "ab" precedes "cd" logically but sits to its left, not to the right a right-to-left word's
            // predecessor would; anchored to "ab"'s left edge the space would land in front of "ab".
            var ops = await PaintTextAsync("""<p>ab <bdo dir="rtl">cd</bdo></p>""");

            Assert.Equal(["ab", " ", "dc"], ops.Select(o => o.Text));
            Assert.True(ops[1].Bounds.Left >= ops[0].Bounds.Right - Tolerance, "the space must not precede 'ab'");
        }

        [Fact]
        public async Task SidewaysVerticalWords_PaintTheSpaceInTheGapDownTheColumn()
        {
            // vertical-rl with the default text-orientation:mixed draws Latin words rotated 90°, each
            // inside its own transform. The space is drawn in the rotated frame, so it is mapped back
            // through that transform before comparing it with the two words' physical extents.
            var ops = await PaintAllAsync(
                """<p style="writing-mode:vertical-rl; height:400pt">AA BB</p>""");

            var placed = PhysicalDrawStrings(ops);
            Assert.Equal(["AA", " ", "BB"], placed.Select(p => p.Text));
            AssertBetween(placed[1].Top, placed[1].Bottom, placed[0].Bottom, placed[2].Top);
        }

        [Fact]
        public async Task SidewaysVerticalWords_DrawTheSpaceInTheFontThatMeasuredTheGap()
        {
            // The vertical flow records the previous word's box as the separator's source, the same
            // as the horizontal one: a's 40pt trailing space fills the gap down the column.
            var ops = await PaintAllAsync(
                """<p style="writing-mode:vertical-rl; height:400pt"><span style="font-size:40pt">a </span><span style="font-size:8pt">b</span></p>""");

            var placed = PhysicalDrawStrings(ops);
            Assert.Equal(["a", " ", "b"], placed.Select(p => p.Text));
            Assert.Equal(placed[2].Top - placed[0].Bottom, placed[1].Bottom - placed[1].Top, 2);
        }

        [Fact]
        public async Task UprightVerticalWords_PaintTheSpaceAboveTheFollowingWord()
        {
            // text-orientation:upright paints one character per call, top to bottom.
            var ops = await PaintTextAsync(
                """<p style="writing-mode:vertical-rl; text-orientation:upright; height:400pt">AB CD</p>""");

            Assert.Equal(["A", "B", " ", "C", "D"], ops.Select(o => o.Text));
            AssertBetween(ops[2].Bounds.Top, ops[2].Bounds.Top, ops[1].Bounds.Top, ops[3].Bounds.Top);
            Assert.True(ops[2].Bounds.Top < ops[3].Bounds.Top - Tolerance, "the space must precede the next word");
        }

        [Fact]
        public async Task TruncatedWord_KeepsTheSpaceInFrontOfItsKeptPart()
        {
            // text-overflow draws the cut word's kept part through its own path; the separator in front
            // of it must not be lost there. The recorder's default MeasureString is a flat zero width,
            // which text-overflow's visibility guard reads as clipped away (see
            // TextOverflowPaintIntegrationTests), so this one measures proportionally.
            var ops = await PaintTextAsync(
                """<p style="width:80pt; white-space:nowrap; overflow:hidden; text-overflow:ellipsis">AA BBBBBBBBBBBBBBBBBBBB</p>""",
                (str, _, _) => new Size((str?.Length ?? 0) * 6.0, 12));

            var text = string.Concat(ops.Select(o => o.Text));
            Assert.StartsWith("AA B", text);
            Assert.EndsWith("…", text);
        }

        [Fact]
        public async Task FontWithoutASpaceGlyph_ShowsNoSeparator()
        {
            // The monochrome emoji subset maps no U+0020. Drawing a space with it would show the
            // missing-glyph box on the page (and fail PDF/A outright), so the separator is left out.
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("""<p style="font-family:EmojiOnly">AA BB</p>"""),
                configureAdapter: adapter => BundledFonts.RegisterFont(adapter, BundledFonts.Emoji, "EmojiOnly"));
            using var recorder = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintPage(container, recorder);

            var ops = recorder.Log.Where(o => o.Kind == PaintOpKind.DrawString).ToList();
            Assert.Equal(["AA", "BB"], ops.Select(o => o.Text));
        }

        [Fact]
        public async Task LargerFollowingWord_DrawsTheSpaceInTheFontThatMeasuredTheGap()
        {
            // The gap after "a" is a's own trailing space, measured in the paragraph's font. Drawn in
            // the following word's 150pt font, the glyph would be far wider than the gap - covering
            // "a" and, at the line start, reaching off the page - and position-aware extractors would
            // drop it again.
            var ops = await PaintTextAsync("""<p>a <span style="font-size:150pt">B</span></p>""");

            Assert.Equal(["a", " ", "B"], ops.Select(o => o.Text));
            AssertBetween(ops[1].Bounds.Left, ops[1].Bounds.Right, ops[0].Bounds.Right, ops[2].Bounds.Left);
        }

        [Theory]
        // the previous word's own trailing space, measured in the previous word's 40pt font
        [InlineData("""<p><span style="font-size:40pt">a </span><span style="font-size:8pt">b</span></p>""")]
        // a white-space-only inline box, measured in the paragraph's 40pt font
        [InlineData("""<p style="font-size:40pt"><span style="font-size:8pt">a</span> <span style="font-size:8pt">b</span></p>""")]
        // a child's own leading space, measured in that child's 8pt font
        [InlineData("""<p style="font-size:40pt"><span style="font-size:8pt">a</span><span style="font-size:8pt"> b</span></p>""")]
        public async Task SeparatorGlyph_IsExactlyAsWideAsTheGapItWasMeasuredFor(string body)
        {
            // Each source of a word separator measures it in a different box's font; the glyph has to
            // come from that same font to fill the gap, however large the words on either side are.
            var ops = await PaintTextAsync(body);

            Assert.Equal(["a", " ", "b"], ops.Select(o => o.Text));
            Assert.Equal(ops[2].Bounds.Left - ops[0].Bounds.Right, ops[1].Bounds.Width, 3);
            Assert.Equal(ops[0].Bounds.Right, ops[1].Bounds.Left, 3);
        }

        [Fact]
        public async Task SpaceBeforeASmallerWord_SitsOnTheWordsBaseline()
        {
            // The separator is drawn in a different font than the word after it, so it is placed by
            // baseline, not by top edge: a top-aligned 30pt space beside a 10pt word would sit far
            // above the line.
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                """<p><span style="font-size:30pt">A </span><span style="font-size:10pt">b</span></p>"""));
            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var calls = g.DrawStringCalls;
            Assert.Equal(["A", " ", "b"], calls.Select(c => c.Text));
            Assert.Equal(calls[2].PaintPoint.Y + calls[2].Font.Ascent, calls[1].PaintPoint.Y + calls[1].Font.Ascent, 3);
        }

        [Fact]
        public async Task SynthesizedSuperscript_ShiftsTheSpaceWithItsWords()
        {
            // The bundled math font has no 'sups' feature, so the superscript is synthesized: the words
            // move off the baseline by the font's own offset, and the space between them has to as well.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                """<p style="font-family:NoSups"><span id="s" style="font-variant-position:super">42 42</span></p>"""),
                configureAdapter: adapter => BundledFonts.RegisterFont(adapter, BundledFonts.Math, "NoSups"));
            Assert.NotNull(LayoutHarness.FindById(root, "s")!.SubSuperscriptSynthesis);
            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var calls = g.DrawStringCalls;
            Assert.Equal(["42", " ", "42"], calls.Select(c => c.Text));
            Assert.Equal(calls[2].PaintPoint.Y + calls[2].Font.Ascent, calls[1].PaintPoint.Y + calls[1].Font.Ascent, 3);
        }

        [Fact]
        public async Task SynthesizedSuperscriptInUprightVerticalText_ShiftsTheSpaceWithItsWords()
        {
            // Upright, each character is drawn on its own; the space above "2" moves with it.
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                """<p style="font-family:NoSups; writing-mode:vertical-rl; text-orientation:upright; height:400pt"><span id="s" style="font-variant-position:super">4 2</span></p>"""),
                configureAdapter: adapter => BundledFonts.RegisterFont(adapter, BundledFonts.Math, "NoSups"));
            var span = LayoutHarness.FindById(root, "s")!;
            var shift = span.SubSuperscriptSynthesis!.Value.BaselineShift;
            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var calls = g.DrawStringCalls;
            Assert.Equal(["4", " ", "2"], calls.Select(c => c.Text));
            Assert.NotEqual(0, shift, 3);
            // Its own advance above the word, moved by the same shift as the word's characters.
            var two = AllWords(span).Last();
            Assert.Equal(two.Top - calls[1].Size.Height + shift, calls[1].PaintPoint.Y, 3);
        }

        [Fact]
        public async Task SynthesizedSuperscriptInSidewaysVerticalText_ShiftsTheSpaceWithItsWords()
        {
            // Sideways, each word is drawn in its own rotated frame, where the space shares its
            // baseline - shifted by the synthesized superscript - with the word after it.
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                """<p style="font-family:NoSups; writing-mode:vertical-rl; height:400pt"><span style="font-variant-position:super">42 42</span></p>"""),
                configureAdapter: adapter => BundledFonts.RegisterFont(adapter, BundledFonts.Math, "NoSups"));
            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var calls = g.DrawStringCalls;
            Assert.Equal(["42", " ", "42"], calls.Select(c => c.Text));
            Assert.Equal(calls[2].PaintPoint.Y + calls[2].Font.Ascent, calls[1].PaintPoint.Y + calls[1].Font.Ascent, 3);
        }

        [Fact]
        public async Task UprightVerticalRightToLeftWords_PaintTheSpaceBelowTheFollowingWord()
        {
            // Under bdo rtl the logically second word "CD" is painted above "AB" in the column, so its
            // logical predecessor - and the gap between them - is below it.
            var ops = await PaintTextAsync(
                """<p style="writing-mode:vertical-rl; text-orientation:upright; height:400pt"><bdo dir="rtl">AB CD</bdo></p>""");

            Assert.Equal(["B", "A", " ", "D", "C"], ops.Select(o => o.Text));
            Assert.True(ops[3].Bounds.Top < ops[0].Bounds.Top, "the logically second word must sit above");
            AssertBetween(ops[2].Bounds.Top, ops[2].Bounds.Top, ops[4].Bounds.Top, ops[0].Bounds.Top);
        }

        [Fact]
        public async Task UprightVerticalSpace_IsCentredAcrossTheColumn()
        {
            var ops = await PaintTextAsync(
                """<p style="writing-mode:vertical-rl; text-orientation:upright; height:400pt; font-size:30pt">AB CD</p>""");

            // The recorder measures every string as zero wide, so each upright character is drawn at the
            // column's centre line; the space has a real width and must straddle that same line.
            var space = Assert.Single(ops, o => o.Text == " ");
            var centre = ops.First(o => o.Text == "C").Bounds.Left;
            Assert.True(space.Bounds.Width > 0);
            Assert.Equal(centre, space.Bounds.Left + space.Bounds.Width / 2, 3);
        }

        [Theory]
        [InlineData("horizontal-tb")]
        [InlineData("vertical-rl")]
        public async Task WordOpeningALine_IsNotRecordedAsPrecededByASeparator(string writingMode)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                $"""<p id="p" style="writing-mode:{writingMode}; width:40pt; height:40pt">Premium Widget</p>"""));
            var p = LayoutHarness.FindById(root, "p")!;
            var words = p.Words.Count > 0 ? p.Words : p.Boxes.First(b => b.Words.Count > 0).Words;

            Assert.Equal(2, words.Count);
            Assert.NotSame(words[0].Line, words[1].Line);
            Assert.False(words[1].PrecededByWordSeparator);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>A 1x1 PNG - only that it decodes matters.</summary>
        private const string OnePixelPng =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8"
            + "z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

        private static IEnumerable<CssRect> AllWords(CssBox box) =>
            box.Words.Concat(box.Boxes.SelectMany(AllWords));

        private static async Task<List<PaintOp>> PaintAllAsync(
            string body, Func<string, Font, ShapeSettings?, Size>? measureString = null)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body));
            using var recorder = new RecordingGraphics(new PdfSharpAdapter()) { MeasureStringOverride = measureString };
            FragmentPaintHarness.PaintPage(container, recorder);
            return recorder.Log;
        }

        private static async Task<List<PaintOp>> PaintTextAsync(
            string body, Func<string, Font, ShapeSettings?, Size>? measureString = null) =>
            (await PaintAllAsync(body, measureString)).Where(o => o.Kind == PaintOpKind.DrawString).ToList();

        /// <summary>
        /// Every <c>DrawString</c> with its bounds mapped through whatever transform is active at the
        /// time, so a rotated run can be compared with its neighbours in page space.
        /// </summary>
        private static List<(string Text, double Top, double Bottom)> PhysicalDrawStrings(IEnumerable<PaintOp> ops)
        {
            var transforms = new Stack<Matrix3x2>();
            var current = Matrix3x2.Identity;
            List<(string, double, double)> placed = [];

            foreach (var op in ops)
            {
                switch (op.Kind)
                {
                    case PaintOpKind.PushTransform:
                        transforms.Push(current);
                        current = op.Matrix!.Value * current;
                        break;
                    case PaintOpKind.PopTransform:
                        current = transforms.Pop();
                        break;
                    case PaintOpKind.DrawString:
                        var a = Vector2.Transform(new Vector2((float)op.Bounds.Left, (float)op.Bounds.Top), current);
                        var b = Vector2.Transform(new Vector2((float)op.Bounds.Right, (float)op.Bounds.Bottom), current);
                        placed.Add((op.Text!, System.Math.Min(a.Y, b.Y), System.Math.Max(a.Y, b.Y)));
                        break;
                }
            }

            return placed;
        }

        private static void AssertBetween(double start, double end, double gapStart, double gapEnd)
        {
            Assert.True(start >= gapStart - Tolerance && end <= gapEnd + Tolerance,
                $"[{start}, {end}] must lie inside the gap [{gapStart}, {gapEnd}]");
        }
    }
}
