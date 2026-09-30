using PeachDrawing.Text.Layout;
using PeachDrawing.Text.Unicode;
using PeachPDF.Tests.TestSupport;
using System.Drawing;

namespace PeachDrawing.Text.Tests.Layout
{
    /// <summary>
    /// Laying a paragraph out: line breaking at a width, alignment, mixed directions, and what an editor asks of the result (carets,
    /// hit tests, selections).
    /// </summary>
    public class ParagraphLayoutTests
    {
        private const double Size = 20;
        private const string Hebrew = "אבג";

        private static Typeface Face(string path)
        {
            var set = new FontSet();
            var family = set.AddFile(path, new AddOptions { FamilyName = "Layout-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static readonly Typeface Latin = Face(BundledFonts.Ttf);
        private static readonly Typeface HebrewFace = Face(BundledFonts.Hebrew);

        private static Paragraph Build(string text, ParagraphStyle? style = null, Typeface? typeface = null)
        {
            var builder = new ParagraphBuilder(new RunStyle(typeface ?? Latin, Size));
            if (style is { } given)
            {
                builder.SetStyle(given);
            }

            return builder.AddText(text).Build();
        }

        private static double Advance(string text) => TextRuler.WidthOf(Latin, Size, text);

        // ---- lines ------------------------------------------------------------------------------------------------------------------

        [Fact]
        public void ShortText_IsOneLine_AsWideAsItsAdvance()
        {
            var layout = Build("Hello world").Layout(1000);

            var line = Assert.Single(layout.Lines);
            Assert.Equal(new TextRange(0, 11), line.Range);
            Assert.Equal(LineEnd.Last, line.End);
            Assert.Equal(Advance("Hello world"), line.Width, 3);
            Assert.True(line.Ascent > 0 && line.Descent > 0);
            Assert.Equal(line.Height, layout.Height, 6);
            Assert.Equal(line.Top + line.Ascent, line.Baseline, 6);
        }

        [Fact]
        public void EmptyText_IsOneEmptyLine()
        {
            var layout = Build("").Layout(100);

            var line = Assert.Single(layout.Lines);
            Assert.True(line.Range.IsEmpty);
            Assert.Equal(0, line.Width);
            Assert.True(line.Height > 0);
        }

        [Fact]
        public void TextThatDoesNotFit_BreaksAtSpaces_AndEveryLineFits()
        {
            var text = "alpha beta gamma delta epsilon zeta";
            double width = Advance("alpha beta") + 1;
            var layout = Build(text).Layout(width);

            Assert.True(layout.Lines.Count > 2);
            int expected = 0;
            foreach (var line in layout.Lines)
            {
                Assert.Equal(expected, line.Range.Start);
                Assert.True(line.Width <= width, $"line {line.Range} is {line.Width} wide");
                expected = line.Range.End;
            }

            Assert.Equal(text.Length, expected);
            Assert.All(layout.Lines.Take(layout.Lines.Count - 1), l => Assert.Equal(LineEnd.Soft, l.End));
            Assert.Equal(LineEnd.Last, layout.Lines[^1].End);
            for (int i = 1; i < layout.Lines.Count; i++)
            {
                Assert.Equal(layout.Lines[i - 1].Top + layout.Lines[i - 1].Height, layout.Lines[i].Top, 6);
            }
        }

        [Fact]
        public void TheSpaceAtASoftBreak_HangsAndIsNotCounted()
        {
            var layout = Build("alpha beta").Layout(Advance("alpha") + 1);

            Assert.Equal(2, layout.Lines.Count);
            var first = layout.Lines[0];
            Assert.Equal(new TextRange(0, 6), first.Range);
            Assert.Equal(5, first.ContentEnd);
            Assert.Equal(Advance("alpha"), first.Width, 3);
        }

        [Fact]
        public void ANewline_ForcesABreak()
        {
            var layout = Build("one\ntwo").Layout(1000);

            Assert.Equal(2, layout.Lines.Count);
            Assert.Equal(LineEnd.Forced, layout.Lines[0].End);
            Assert.Equal(new TextRange(0, 4), layout.Lines[0].Range);
            Assert.Equal(Advance("one"), layout.Lines[0].Width, 3);
            Assert.Equal(LineEnd.Last, layout.Lines[1].End);
        }

        [Fact]
        public void TextEndingInANewline_HasAnEmptyLastLine_ToPutACaretOn()
        {
            var layout = Build("one\n").Layout(1000);

            Assert.Equal(2, layout.Lines.Count);
            Assert.Equal(new TextRange(4, 4), layout.Lines[1].Range);
            var caret = layout.CaretRect(new TextPosition(4));
            Assert.Equal((float)layout.Lines[1].Top, caret.Top);
        }

        [Fact]
        public void NoWrap_BreaksOnlyAtForcedBreaks()
        {
            var layout = Build("alpha beta gamma\ndelta", new ParagraphStyle { NoWrap = true }).Layout(10);

            Assert.Equal(2, layout.Lines.Count);
        }

        [Fact]
        public void AnUnlimitedWidth_LaysOutWithoutWrapping()
        {
            var layout = Build("alpha beta gamma delta").Layout(double.PositiveInfinity);

            var line = Assert.Single(layout.Lines);
            Assert.Equal(layout.ContentWidth, layout.Width, 6);
            Assert.Equal(line.Width, layout.Width, 6);
        }

        // ---- long words -------------------------------------------------------------------------------------------------------------

        [Fact]
        public void ALongWord_OverflowsByDefault()
        {
            var layout = Build("Supercalifragilistic").Layout(Advance("Super"));

            var line = Assert.Single(layout.Lines);
            Assert.True(line.Width > layout.Width);
        }

        [Theory]
        [InlineData(OverflowWrap.BreakWord)]
        [InlineData(OverflowWrap.Anywhere)]
        public void ALongWord_IsCutBetweenCharacters_WhenAllowed(OverflowWrap wrap)
        {
            var text = "Supercalifragilistic";
            double width = Advance("Superc") + 1;
            var layout = Build(text, new ParagraphStyle { OverflowWrap = wrap }).Layout(width);

            Assert.True(layout.Lines.Count > 1);
            Assert.All(layout.Lines, l => Assert.True(l.Width <= width));
            Assert.Equal(LineEnd.Emergency, layout.Lines[0].End);
            Assert.Equal(text.Length, layout.Lines[^1].Range.End);
        }

        [Fact]
        public void AWordThatFitsALineOfItsOwn_IsMovedNotCut()
        {
            var layout = Build("ab Supercalifragilistic", new ParagraphStyle { OverflowWrap = OverflowWrap.BreakWord })
                .Layout(Advance("Supercalifragilistic") + 1);

            Assert.Equal(2, layout.Lines.Count);
            Assert.Equal(LineEnd.Soft, layout.Lines[0].End);
        }

        // ---- alignment --------------------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData(TextAlign.Start, 0)]
        [InlineData(TextAlign.Left, 0)]
        [InlineData(TextAlign.End, 1)]
        [InlineData(TextAlign.Right, 1)]
        [InlineData(TextAlign.Center, 0.5)]
        public void ALeftToRightLine_IsAlignedWithinTheWidth(TextAlign align, double fraction)
        {
            var layout = Build("Hello", new ParagraphStyle { Align = align, Direction = BaseDirection.Ltr }).Layout(300);

            var line = layout.Lines[0];
            Assert.Equal((300 - line.Width) * fraction, line.Left, 3);
            Assert.Equal(line.Left, line.Runs[0].X, 6);
        }

        // ---- directions -------------------------------------------------------------------------------------------------------------

        [Fact]
        public void HebrewText_IsARightToLeftRun_AlignedAtTheRightByDefault()
        {
            var paragraph = Build(Hebrew, typeface: HebrewFace);
            var layout = paragraph.Layout(300);

            Assert.True(paragraph.IsRightToLeft);
            var run = Assert.Single(layout.Lines[0].Runs);
            Assert.True(run.IsRightToLeft);
            Assert.Equal(300, run.X + run.Width, 3);

            // The caret at the start of the text is at the run's right edge, and at its end at the left.
            Assert.Equal(run.X + run.Width, run.GetCaretX(0), 3);
            Assert.Equal(run.X, run.GetCaretX(Hebrew.Length), 3);
        }

        [Fact]
        public void MixedText_PutsTheRunsInVisualOrder()
        {
            var paragraph = Build("abc " + Hebrew + " def", new ParagraphStyle { Direction = BaseDirection.Ltr });
            var runs = paragraph.Layout(1000).Lines[0].Runs;

            // The Latin words and the Hebrew are separate runs; the space beyond the Hebrew may be one of its own.
            var levels = runs.Select(r => r.Level).ToList();
            Assert.Equal(0, levels[0]);
            Assert.Equal(1, levels[1]);
            Assert.All(levels.Skip(2), l => Assert.Equal(0, l));
            Assert.Equal(new[] { 0, 4, 7 }, runs.Take(3).Select(r => r.Range.Start).ToArray());
            for (int i = 1; i < runs.Count; i++)
            {
                Assert.Equal(runs[i - 1].X + runs[i - 1].Width, runs[i].X, 6);
            }
        }

        [Fact]
        public void ARightToLeftParagraph_ReversesTheOrderOfItsRuns()
        {
            var paragraph = Build("abc " + Hebrew + " def", new ParagraphStyle { Direction = BaseDirection.Rtl });
            var runs = paragraph.Layout(1000).Lines[0].Runs;

            // Logical order abc, Hebrew, def reads right to left, so def is drawn leftmost.
            Assert.Equal("def", paragraph.Text.Substring(runs[0].Range.Start, runs[0].Range.Length).Trim());
            Assert.Equal("abc", paragraph.Text.Substring(runs[^1].Range.Start, runs[^1].Range.Length).Trim());
        }

        // ---- styles -----------------------------------------------------------------------------------------------------------------

        [Fact]
        public void RunsOfDifferentSizes_MakeALineAsTallAsTheLargest()
        {
            var builder = new ParagraphBuilder(new RunStyle(Latin, 10));
            builder.AddText("small ").PushRun(new RunStyle(Latin, 40)).AddText("BIG").PopRun().AddText(" small");
            var mixed = builder.Build().Layout(1000);
            var small = Build("small").Layout(1000);

            Assert.Equal(3, mixed.Lines[0].Runs.Count);
            Assert.True(mixed.Lines[0].Height > small.Lines[0].Height * 1.5);
            Assert.All(mixed.Lines[0].Runs, r => Assert.Equal(mixed.Lines[0].Baseline, r.Baseline, 6));
        }

        [Fact]
        public void ALineHeightMultiple_SetsTheHeight_AndSharesTheLeading()
        {
            var layout = Build("Hello", new ParagraphStyle { LineHeight = 2 }).Layout(1000);

            var line = layout.Lines[0];
            Assert.Equal(2 * Size, line.Height, 6);
            Assert.Equal(line.Height, line.Ascent + line.Descent, 6);
        }

        // ---- editing queries --------------------------------------------------------------------------------------------------------

        [Fact]
        public void HitTesting_FindsTheBoundaryNearestThePoint_ForEveryCaret()
        {
            var layout = Build("Hello world").Layout(1000);

            for (int i = 0; i <= 11; i++)
            {
                var caret = layout.CaretRect(new TextPosition(i));
                var found = layout.PositionAt(new PointF(caret.X, caret.Y + 1));
                Assert.Equal(i, found.Index);
            }
        }

        [Fact]
        public void HitTesting_ClampsToTheEdges()
        {
            var layout = Build("alpha beta gamma").Layout(Advance("alpha beta") + 1);

            Assert.Equal(0, layout.PositionAt(new PointF(-50, -50)).Index);
            Assert.Equal(16, layout.PositionAt(new PointF(5000, 5000)).Index);
            Assert.Equal(layout.Lines[1].Range.Start, layout.PositionAt(new PointF(-1, (float)layout.Lines[1].Top + 1)).Index);
        }

        [Fact]
        public void HitTesting_InRightToLeftText_ReadsFromTheRight()
        {
            var layout = Build(Hebrew, typeface: HebrewFace).Layout(300);

            Assert.Equal(0, layout.PositionAt(new PointF(299, 1)).Index);
            Assert.Equal(Hebrew.Length, layout.PositionAt(new PointF(1, 1)).Index);
        }

        [Fact]
        public void ACaretAtASoftBreak_IsAtTheEndOfTheEarlierLine_OrTheStartOfTheLater()
        {
            var layout = Build("alpha beta").Layout(Advance("alpha") + 1);
            int at = layout.Lines[0].Range.End;

            var upstream = layout.CaretRect(new TextPosition(at, TextAffinity.Upstream));
            var downstream = layout.CaretRect(new TextPosition(at, TextAffinity.Downstream));

            Assert.Equal((float)layout.Lines[0].Top, upstream.Top);
            Assert.Equal((float)layout.Lines[1].Top, downstream.Top);
            Assert.Equal((float)layout.Lines[1].Left, downstream.X, 2);
        }

        [Fact]
        public void ASelectionWithinALine_IsOneBoxBetweenTheCarets()
        {
            var layout = Build("Hello world").Layout(1000);

            var box = Assert.Single(layout.SelectionBoxes(new TextRange(2, 7)));

            Assert.Equal(layout.CaretRect(new TextPosition(2)).X, box.Left, 2);
            Assert.Equal(layout.CaretRect(new TextPosition(7)).X, box.Right, 2);
            Assert.Equal((float)layout.Lines[0].Height, box.Height);
        }

        [Fact]
        public void ASelectionAcrossLines_HasABoxOnEach()
        {
            var layout = Build("alpha beta gamma").Layout(Advance("alpha beta") + 1);

            var boxes = layout.SelectionBoxes(new TextRange(2, 14));

            Assert.Equal(layout.Lines.Count, boxes.Count);
            Assert.True(boxes[0].Top < boxes[1].Top);
        }

        [Fact]
        public void ASelectionInMixedText_CanBeSeveralBoxesOnALine()
        {
            var paragraph = Build("ab " + Hebrew + " cd", new ParagraphStyle { Direction = BaseDirection.Ltr });
            var layout = paragraph.Layout(1000);

            // From the middle of "ab" to the middle of "cd" is one visually contiguous stretch here.
            var boxes = layout.SelectionBoxes(new TextRange(1, 7));
            Assert.NotEmpty(boxes);
            Assert.All(boxes, b => Assert.True(b.Width > 0));
        }

        [Fact]
        public void WordAndGraphemeRanges_FollowUax29()
        {
            var layout = Build("hello wide éx").Layout(1000);

            Assert.Equal(new TextRange(0, 5), layout.WordRangeAt(2));
            Assert.Equal(new TextRange(5, 6), layout.WordRangeAt(5));
            Assert.Equal(new TextRange(11, 13), layout.GraphemeRangeAt(11));
            Assert.Equal(new TextRange(11, 13), layout.GraphemeRangeAt(12));
        }

        [Fact]
        public void APositionInsideACombiningSequence_IsNeverReturnedByAHitTest()
        {
            var layout = Build("éé").Layout(1000);

            for (float x = 0; x < layout.Width; x += 0.5f)
            {
                Assert.NotEqual(1, layout.PositionAt(new PointF(x, 1)).Index);
                Assert.NotEqual(3, layout.PositionAt(new PointF(x, 1)).Index);
            }
        }

        [Fact]
        public void ArabicText_IsShapedWithItsJoiningForms()
        {
            var arabic = Face(BundledFonts.Arabic);
            // "sin lam alef meem": the letters join, so the glyphs are the joined forms and the run is right to left.
            var text = "سلام";
            var run = Assert.Single(Build(text, typeface: arabic).Layout(500).Lines[0].Runs);

            Assert.True(run.IsRightToLeft);
            Assert.True(run.Width > 0);
            var isolated = TextRuler.WidthOf(arabic, Size, "س");
            Assert.True(run.Width < 4 * isolated * 1.5);
        }

        [Fact]
        public void DevanagariText_IsShapedAsSyllables()
        {
            var devanagari = Face(BundledFonts.Devanagari);
            var text = "क्षि";      // ksha + vowel sign i, which reorders
            var layout = Build(text, typeface: devanagari).Layout(500);

            var run = Assert.Single(layout.Lines[0].Runs);
            Assert.True(run.Width > 0);
            Assert.Equal(new TextRange(0, text.Length), run.Range);
            Assert.True(run.GetCaretX(text.Length) >= run.GetCaretX(0));
        }

        [Fact]
        public void ACaretInsideALigatureCluster_IsSharedOutBetweenItsCharacters()
        {
            var layout = Build("ffi office").Layout(1000);
            var run = layout.Lines[0].Runs[0];

            for (int i = 1; i <= 10; i++)
            {
                Assert.True(run.GetCaretX(i) >= run.GetCaretX(i - 1) - 1e-6);
            }

            Assert.Equal(run.X + run.Width, run.GetCaretX(10), 3);
        }

        [Fact]
        public void HitTesting_RightOfASoftWrappedRightToLeftLine_StaysOnThatLine()
        {
            var text = Hebrew + Hebrew;    // no space: an emergency cut
            var paragraph = Build(text, new ParagraphStyle { OverflowWrap = OverflowWrap.BreakWord }, HebrewFace);
            var layout = paragraph.Layout(TextRuler.WidthOf(HebrewFace, Size, Hebrew) + 1);
            Assert.True(layout.Lines.Count > 1);

            var line = layout.Lines[1];
            var found = layout.PositionAt(new PointF((float)layout.Width + 50, (float)line.Top + 1));

            Assert.Equal(line.Range.Start, found.Index);
            Assert.Equal((float)line.Top, layout.CaretRect(found).Top);
        }

        [Fact]
        public void TheCaretInHangingSpaces_IsAtTheEndOfTheLine_InTheParagraphsDirection()
        {
            var ltr = Build("abc " + Hebrew + " ", new ParagraphStyle { Direction = BaseDirection.Ltr }, typeface: null).Layout(1000);
            var last = ltr.Lines[0].Runs[^1];
            Assert.Equal(last.X + last.Width, ltr.CaretRect(new TextPosition(ltr.Paragraph.Text.Length)).X, 2);

            var rtl = Build(Hebrew + " abc ", new ParagraphStyle { Direction = BaseDirection.Rtl }).Layout(1000);
            Assert.Equal(rtl.Lines[0].Runs[0].X, rtl.CaretRect(new TextPosition(rtl.Paragraph.Text.Length)).X, 2);
        }

        [Fact]
        public void AnEmergencyCut_LetsTheCaretChooseALine()
        {
            var layout = Build("Supercalifragilistic", new ParagraphStyle { OverflowWrap = OverflowWrap.BreakWord }).Layout(Advance("Superc") + 1);
            int at = layout.Lines[0].Range.End;

            Assert.Equal((float)layout.Lines[0].Top, layout.CaretRect(new TextPosition(at, TextAffinity.Upstream)).Top);
            Assert.Equal((float)layout.Lines[1].Top, layout.CaretRect(new TextPosition(at, TextAffinity.Downstream)).Top);
        }

        [Fact]
        public void ASelectionRangeGivenBackwards_IsTheSameSelection()
        {
            var layout = Build("Hello world").Layout(1000);

            Assert.Equal(layout.SelectionBoxes(new TextRange(2, 7)), layout.SelectionBoxes(new TextRange(7, 2)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-3)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void ASizeThatIsNotPositiveAndFinite_IsRejected(double size)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ParagraphBuilder(new RunStyle(Latin, size)));
        }

        [Fact]
        public void AHitTestWithANaNPoint_IsRejected()
        {
            var layout = Build("x").Layout(100);

            Assert.Throws<ArgumentOutOfRangeException>(() => layout.PositionAt(new PointF(float.NaN, 0)));
        }

        [Fact]
        public void DefaultParagraphStyle_IsLeftToRightAndWraps()
        {
            var layout = new ParagraphBuilder(new RunStyle(Latin, Size)).SetStyle(default).AddText("alpha beta gamma").Build().Layout(Advance("alpha") + 1);

            Assert.True(layout.Lines.Count > 1);
            Assert.False(layout.Paragraph.IsRightToLeft);
        }

        // ---- content widths ---------------------------------------------------------------------------------------------------------

        [Fact]
        public void ContentWidths_AreTheWidestWord_AndTheWholeLine()
        {
            var widths = Build("alpha beta Supercalifragilistic z").MeasureContent();

            Assert.Equal(Advance("Supercalifragilistic"), widths.MinContent, 3);
            Assert.Equal(Advance("alpha beta Supercalifragilistic z"), widths.MaxContent, 3);
        }

        [Fact]
        public void ContentWidths_WithForcedBreaks_TakeTheWidestLine()
        {
            var widths = Build("ab\nabcdef\nabc").MeasureContent();

            Assert.Equal(Advance("abcdef"), widths.MaxContent, 3);
        }

        [Fact]
        public void OverflowWrapAnywhere_LetsAParagraphBeAsNarrowAsOneCharacter()
        {
            var widths = Build("Supercalifragilistic", new ParagraphStyle { OverflowWrap = OverflowWrap.Anywhere }).MeasureContent();

            Assert.True(widths.MinContent < Advance("Supercalifragilistic") / 5);
        }

        // ---- the result -------------------------------------------------------------------------------------------------------------

        [Fact]
        public void ALayout_IsASnapshot_AndTheParagraphCanBeLaidOutAgainAtAnotherWidth()
        {
            var paragraph = Build("alpha beta gamma delta");

            var narrow = paragraph.Layout(Advance("alpha") + 1);
            var wide = paragraph.Layout(10000);
            var narrowAgain = paragraph.Layout(Advance("alpha") + 1);

            Assert.True(narrow.Lines.Count > 1);
            Assert.Single(wide.Lines);
            Assert.Equal(narrow.Lines.Select(l => l.Range), narrowAgain.Lines.Select(l => l.Range));
            Assert.Equal(narrow.Height, narrowAgain.Height, 6);
        }

        [Fact]
        public void Arguments_AreChecked()
        {
            var paragraph = Build("x");

            Assert.Throws<ArgumentOutOfRangeException>(() => paragraph.Layout(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => paragraph.Layout(double.NaN));
            var layout = paragraph.Layout(100);
            Assert.Throws<ArgumentOutOfRangeException>(() => layout.CaretRect(new TextPosition(5)));
            Assert.Throws<ArgumentOutOfRangeException>(() => layout.WordRangeAt(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => layout.Lines[0].Runs[0].GetCaretX(9));
            Assert.Throws<InvalidOperationException>(() => new ParagraphBuilder(new RunStyle(Latin, 10)).PopRun());
            Assert.Throws<ArgumentNullException>(() => TextRuler.WidthOf(Latin, 10, null!));
        }

        [Fact]
        public void TextRuler_MatchesTheShapedAdvance()
        {
            Assert.Equal(Advance("Hello"), Build("Hello").Layout(1000).Lines[0].Width, 3);
            Assert.True(Advance("Hello") > Advance("Hell"));
        }
    }
}
