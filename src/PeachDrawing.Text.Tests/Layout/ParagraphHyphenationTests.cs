using PeachDrawing.Text.Layout;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Unicode;
using PeachPDF.Tests.TestSupport;
using System.Diagnostics;
using System.Drawing;
using System.Text;

namespace PeachDrawing.Text.Tests.Layout
{
    /// <summary>Hyphenation: soft hyphens, patterns, the limits, and the hyphen a broken line ends with.</summary>
    public class ParagraphHyphenationTests
    {
        private const double Size = 20;
        private const string Soft = "­";
        private static readonly Typeface Face = Load(BundledFonts.Ttf);
        private static readonly Typeface HebrewFace = Load(BundledFonts.Hebrew);
        private static readonly string HyphenText = Face.TryMapRune(new Rune(0x2010), out _) ? "‐" : "-";
        private static readonly double HyphenWidth = TextRuler.WidthOf(Face, Size, HyphenText);
        private static readonly LineBreakOptions English = new() { Language = "en" };

        private static Typeface Load(string path)
        {
            var set = new FontSet();
            var family = set.AddFile(path, new AddOptions { FamilyName = "Hyphen-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static double Advance(string text) => TextRuler.WidthOf(Face, Size, text);

        private static ParagraphLayout Lay(string text, double width, ParagraphStyle style, Typeface? face = null)
            => new ParagraphBuilder(new RunStyle(face ?? Face, Size)).SetStyle(style).AddText(text).Build().Layout(width);

        private static ParagraphStyle Auto(Func<ParagraphStyle, ParagraphStyle>? tweak = null)
        {
            var style = new ParagraphStyle { Hyphens = Hyphens.Auto, LineBreak = English };
            return tweak is null ? style : tweak(style);
        }

        private static PlacedRun? GeneratedRun(LineBox line) => line.Runs.SingleOrDefault(r => r.IsGenerated);

        private static string Reassemble(ParagraphLayout layout) => string.Concat(layout.Lines.Select(l => layout.Paragraph.Text[l.Range.Start..l.Range.End]));

        // ---- soft hyphens ----------------------------------------------------------------------------------------------------------

        [Fact]
        public void ALineBrokenAtASoftHyphen_EndsWithAHyphenOfItsOwn()
        {
            var text = "hy" + Soft + "phen";
            var layout = Lay(text, Advance("hy") + HyphenWidth + 1, new ParagraphStyle());

            Assert.Equal(2, layout.Lines.Count);
            var first = layout.Lines[0];
            Assert.Equal(LineEnd.Hyphenated, first.End);
            Assert.Equal(new TextRange(0, 3), first.Range);
            var hyphen = GeneratedRun(first);
            Assert.NotNull(hyphen);
            Assert.True(hyphen!.Range.IsEmpty);
            Assert.Equal(3, hyphen.Range.Start);
            Assert.Equal(HyphenWidth, hyphen.Width, 3);
            Assert.Same(first.Runs[^1], hyphen);
            Assert.Equal(Advance("hy") + HyphenWidth, first.Width, 3);
            Assert.Equal(Advance("hy"), first.Runs[0].Width, 2);
            Assert.Null(GeneratedRun(layout.Lines[1]));
            Assert.Equal(3, layout.Lines[1].Range.Start);
        }

        [Fact]
        public void ASoftHyphenThatIsNotBroken_DrawsNothingAndTakesNoRoom()
        {
            var plain = Lay("hyphen", 1000, new ParagraphStyle());
            var soft = Lay("hy" + Soft + "phen", 1000, new ParagraphStyle());

            var line = Assert.Single(soft.Lines);
            Assert.Null(GeneratedRun(line));
            Assert.Equal(plain.Lines[0].Width, line.Width, 2);
        }

        [Fact]
        public void WithHyphensNone_ASoftHyphenIsNotAPlaceToBreak()
        {
            var layout = Lay("hy" + Soft + "phen", Advance("hy") + HyphenWidth + 1, new ParagraphStyle { Hyphens = Hyphens.None });

            var line = Assert.Single(layout.Lines);
            Assert.Null(GeneratedRun(line));
        }

        [Fact]
        public void WhenTheHyphenHasNoRoom_TheLineEndsAtTheLastPlaceThatHasIt()
        {
            var text = "aa bb" + Soft + "cc";
            // "aa bb" fits, but not with a hyphen after it.
            var layout = Lay(text, Advance("aa bb") + (HyphenWidth / 2), new ParagraphStyle());

            Assert.Equal(new TextRange(0, 3), layout.Lines[0].Range);
            Assert.Equal(LineEnd.Soft, layout.Lines[0].End);
            Assert.Null(GeneratedRun(layout.Lines[0]));
        }

        [Fact]
        public void ATooNarrowLine_LetsTheHyphenOverflowRatherThanLoop()
        {
            var layout = Lay("a" + Soft + "b" + Soft + "c" + Soft + "d", 0, new ParagraphStyle());

            Assert.Equal(4, layout.Lines.Count);
            Assert.Equal(LineEnd.Hyphenated, layout.Lines[0].End);
            Assert.Equal(LineEnd.Last, layout.Lines[3].End);
        }

        [Fact]
        public void TheHyphenIsCounted_InWhereLinesBreak_AndInTheWidth()
        {
            var text = "aaa" + Soft + "bbb" + Soft + "ccc";
            double width = Advance("aaabbb") + HyphenWidth + 1;
            var layout = Lay(text, width, new ParagraphStyle());

            Assert.All(layout.Lines, l => Assert.True(l.Width <= width));
            Assert.Equal(new TextRange(0, 8), layout.Lines[0].Range);
        }

        [Fact]
        public void ACustomHyphenationCharacter_IsUsed()
        {
            var layout = Lay("hy" + Soft + "phen", Advance("hy") + Advance("~") + 1, new ParagraphStyle { HyphenateCharacter = "~" });

            var hyphen = GeneratedRun(layout.Lines[0]);
            Assert.NotNull(hyphen);
            Assert.True(Face.TryMapRune(new Rune('~'), out var glyph));
            Assert.Equal(glyph, (ushort)hyphen!.Glyphs.Glyphs[0].GlyphIndex);
            Assert.Equal(Advance("~"), hyphen.Width, 3);
        }

        // ---- automatic hyphenation -------------------------------------------------------------------------------------------------

        [Fact]
        public void AWordThatDoesNotFit_IsHyphenatedByThePatternsOfItsLanguage()
        {
            var text = "The internationalization matters";
            double width = Advance("The internationa") + HyphenWidth;
            var layout = Lay(text, width, Auto());

            Assert.True(layout.Lines.Count >= 2);
            var first = layout.Lines[0];
            Assert.Equal(LineEnd.Hyphenated, first.End);
            Assert.NotNull(GeneratedRun(first));
            Assert.All(layout.Lines, l => Assert.True(l.Width <= width + 1e-6, $"{l.Range} is {l.Width} wide"));
            Assert.Equal(text, Reassemble(layout));
            // The line is as full as the patterns let it be: the word's next piece would not have fitted.
            Assert.True(first.Range.End > 4);
        }

        [Fact]
        public void WithoutALanguage_NothingIsHyphenated()
        {
            var layout = Lay("The internationalization", Advance("The internationa") + HyphenWidth, new ParagraphStyle { Hyphens = Hyphens.Auto });

            Assert.All(layout.Lines, l => Assert.NotEqual(LineEnd.Hyphenated, l.End));
        }

        [Fact]
        public void TheLanguageOfARun_IsTheOneItsWordsAreHyphenatedIn()
        {
            var shape = new ShapeSettings { Language = "en" };
            var builder = new ParagraphBuilder(new RunStyle(Face, Size, shape)).SetStyle(new ParagraphStyle { Hyphens = Hyphens.Auto });
            var layout = builder.AddText("The internationalization").Build().Layout(Advance("The internationa") + HyphenWidth);

            Assert.Contains(layout.Lines, l => l.End == LineEnd.Hyphenated);
        }

        [Fact]
        public void HyphensManual_DoesNotUseThePatterns()
        {
            var layout = Lay("The internationalization", Advance("The internationa") + HyphenWidth, new ParagraphStyle { Hyphens = Hyphens.Manual, LineBreak = English });

            Assert.All(layout.Lines, l => Assert.NotEqual(LineEnd.Hyphenated, l.End));
        }

        [Fact]
        public void AWordLongerThanALine_IsHyphenatedOnALineOfItsOwn()
        {
            var text = "internationalization";
            double width = Advance("interna") + HyphenWidth + 1;
            var layout = Lay(text, width, Auto());

            Assert.True(layout.Lines.Count > 2);
            Assert.All(layout.Lines.Take(layout.Lines.Count - 1), l => Assert.Equal(LineEnd.Hyphenated, l.End));
            Assert.All(layout.Lines, l => Assert.True(l.Width <= width + 1e-6));
            Assert.Equal(text, Reassemble(layout));
        }

        [Fact]
        public void HyphenationIsPreferredToCuttingAWord_WhenBothAreAllowed()
        {
            var layout = Lay("internationalization", Advance("interna") + HyphenWidth + 1, Auto(s => s with { OverflowWrap = OverflowWrap.BreakWord }));

            Assert.Equal(LineEnd.Hyphenated, layout.Lines[0].End);
        }

        [Fact]
        public void ALimitOnTheSizeOfAWord_KeepsShortWordsWhole()
        {
            var style = Auto(s => s with { HyphenateLimitChars = new HyphenateLimitChars(WordLength: 40) });
            var layout = Lay("The internationalization", Advance("The internationa") + HyphenWidth, style);

            Assert.All(layout.Lines, l => Assert.NotEqual(LineEnd.Hyphenated, l.End));
        }

        [Fact]
        public void ALimitOnThePiecesBeforeAndAfter_IsKept()
        {
            var style = Auto(s => s with { HyphenateLimitChars = new HyphenateLimitChars(BeforeBreak: 6, AfterBreak: 6) });
            var layout = Lay("internationalization", Advance("interna") + HyphenWidth + 1, style);

            foreach (var line in layout.Lines.Where(l => l.End == LineEnd.Hyphenated))
            {
                Assert.True(line.Range.Length >= 6);
                Assert.True(layout.Paragraph.Text.Length - line.Range.End >= 6);
            }
        }

        [Fact]
        public void ALimitOnLinesInARow_StopsHyphenatingAfterThatMany()
        {
            var style = Auto(s => s with { HyphenateLimitLines = 1 });
            var layout = Lay("internationalization internationalization", Advance("interna") + HyphenWidth + 1, style);

            for (int i = 1; i < layout.Lines.Count; i++)
            {
                Assert.False(layout.Lines[i - 1].End == LineEnd.Hyphenated && layout.Lines[i].End == LineEnd.Hyphenated);
            }

            Assert.Contains(layout.Lines, l => l.End == LineEnd.Hyphenated);
        }

        [Fact]
        public void ALimitZone_KeepsAWordWholeWhenTheLineIsAlmostFull()
        {
            double width = Advance("aaa internationa") + HyphenWidth;
            var text = "aaa internationalization";
            var without = Lay(text, width, Auto());
            var zoned = Lay(text, width, Auto(s => s with { HyphenateLimitZone = width }));

            Assert.Equal(LineEnd.Hyphenated, without.Lines[0].End);
            Assert.Equal(LineEnd.Soft, zoned.Lines[0].End);
            Assert.Equal(new TextRange(0, 4), zoned.Lines[0].Range);
        }

        [Fact]
        public void TheLastFullLine_IsNotHyphenatedWhenTheLimitSaysSo()
        {
            // Hyphenated near its end, the word would leave a last line of its own; the limit moves the hyphenation earlier.
            double width = Advance("aa internationa") + HyphenWidth + 1;
            var text = "aa internationalization";
            var allowed = Lay(text, width, Auto());
            var limited = Lay(text, width, Auto(s => s with { HyphenateLimitLast = HyphenateLimitLast.Always }));

            Assert.Equal(2, allowed.Lines.Count);
            Assert.Equal(LineEnd.Hyphenated, allowed.Lines[0].End);
            // Only a hyphenation that leaves more than a line behind is allowed, and there is one, early.
            Assert.Equal(LineEnd.Hyphenated, limited.Lines[0].End);
            Assert.True(limited.Lines[0].Range.End < allowed.Lines[0].Range.End);
            Assert.True(limited.Lines[1].Width > width);
        }

        [Fact]
        public void HyphenationLimits_AreValidated()
        {
            var builder = new ParagraphBuilder(new RunStyle(Face, Size));

            Assert.Throws<ArgumentOutOfRangeException>(() => builder.SetStyle(new ParagraphStyle { HyphenateLimitChars = new HyphenateLimitChars(BeforeBreak: -1) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.SetStyle(new ParagraphStyle { HyphenateLimitLines = -1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.SetStyle(new ParagraphStyle { HyphenateLimitZone = double.NaN }));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.SetStyle(new ParagraphStyle { HyphenateLimitZone = -1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.SetStyle(new ParagraphStyle { HyphenateCharacter = "" }));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.SetStyle(new ParagraphStyle { HyphenateCharacter = new string('-', 33) }));
        }

        // ---- editing and other layout ----------------------------------------------------------------------------------------------

        [Fact]
        public void ACaretAtAHyphenatedBreak_CanBeOnEitherLine()
        {
            var layout = Lay("hy" + Soft + "phen", Advance("hy") + HyphenWidth + 1, new ParagraphStyle());

            var upstream = layout.CaretRect(new TextPosition(3, TextAffinity.Upstream));
            var downstream = layout.CaretRect(new TextPosition(3, TextAffinity.Downstream));
            Assert.Equal((float)layout.Lines[0].Top, upstream.Top);
            Assert.Equal((float)layout.Lines[1].Top, downstream.Top);
            // On the first line the caret is before the hyphen.
            Assert.Equal((float)Advance("hy"), upstream.X, 1);
            Assert.Equal(TextAffinity.Upstream, layout.PositionAt(new PointF(1000, 5)).Affinity);
        }

        [Fact]
        public void TheGeneratedRun_HasNoSelectionBox_AndTheTextAroundItDoes()
        {
            var layout = Lay("hy" + Soft + "phen", Advance("hy") + HyphenWidth + 1, new ParagraphStyle());

            var boxes = layout.SelectionBoxes(new TextRange(0, 3));
            var box = Assert.Single(boxes);
            Assert.Equal((float)Advance("hy"), box.Right, 1);
        }

        [Fact]
        public void AJustifiedHyphenatedLine_FillsTheWidth_WithTheHyphenLast()
        {
            var text = "aa bb cc internationalization";
            double width = Advance("aa bb cc interna") + HyphenWidth + 30;
            var style = Auto(s => s with { Align = TextAlign.Justify });
            var layout = Lay(text, width, style);

            var first = layout.Lines[0];
            Assert.Equal(LineEnd.Hyphenated, first.End);
            Assert.Equal(width, first.Width, 3);
            Assert.True(first.Runs[^1].IsGenerated);
            Assert.Equal(first.Left + first.Width, first.Runs[^1].X + first.Runs[^1].Width, 3);
        }

        [Fact]
        public void ARightToLeftParagraph_PutsTheHyphenAtTheLeftEnd()
        {
            var text = "אב" + Soft + "גד";
            double width = TextRuler.WidthOf(HebrewFace, Size, "אב") + TextRuler.WidthOf(HebrewFace, Size, "-") + 1;
            var layout = Lay(text, width, new ParagraphStyle { Direction = BaseDirection.Rtl }, HebrewFace);

            var first = layout.Lines[0];
            Assert.Equal(LineEnd.Hyphenated, first.End);
            Assert.True(first.Runs[0].IsGenerated);
            // The caret at the end of the text is on the hyphen's right edge, where the text meets it.
            Assert.Equal((float)(first.Runs[0].X + first.Runs[0].Width), layout.CaretRect(new TextPosition(3, TextAffinity.Upstream)).X, 1);
        }

        [Fact]
        public void TheMinContent_WithAutomaticHyphenation_IsThePieceThatCannotBeBroken()
        {
            var text = "internationalization";
            double whole = new ParagraphBuilder(new RunStyle(Face, Size)).AddText(text).Build().MeasureContent().MinContent;
            var hyphenated = new ParagraphBuilder(new RunStyle(Face, Size)).SetStyle(Auto()).AddText(text).Build().MeasureContent();

            Assert.True(hyphenated.MinContent < whole);
            Assert.True(hyphenated.MinContent > HyphenWidth);
            // The narrowest layout it names does not overflow.
            var layout = new ParagraphBuilder(new RunStyle(Face, Size)).SetStyle(Auto()).AddText(text).Build().Layout(hyphenated.MinContent + 0.01);
            Assert.All(layout.Lines, l => Assert.True(l.Width <= hyphenated.MinContent + 0.01));
        }

        [Fact]
        public void TheMinContent_CountsTheHyphenOfASoftHyphen()
        {
            var plain = new ParagraphBuilder(new RunStyle(Face, Size)).AddText("hyphen").Build().MeasureContent().MinContent;
            var soft = new ParagraphBuilder(new RunStyle(Face, Size)).AddText("hy" + Soft + "phen").Build().MeasureContent().MinContent;

            Assert.True(soft < plain);
            Assert.True(soft >= Advance("phen"));
            Assert.True(soft >= Advance("hy") + HyphenWidth - 1e-6);
        }

        // ---- hostile input ---------------------------------------------------------------------------------------------------------

        [Fact]
        public void ManySoftHyphens_AtANarrowWidth_FinishQuickly()
        {
            var text = string.Concat(Enumerable.Repeat("a" + Soft, 20_000));
            var stopwatch = Stopwatch.StartNew();
            var layout = Lay(text, Advance("aaaa") + HyphenWidth, new ParagraphStyle());
            stopwatch.Stop();

            Assert.True(layout.Lines.Count > 1000);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60), $"took {stopwatch.Elapsed}");
        }

        [Fact]
        public void AHugeWordOfLetters_IsHyphenatedWithoutHanging()
        {
            var text = new string('a', 3000);
            var stopwatch = Stopwatch.StartNew();
            var layout = Lay(text, 15 * Size, Auto(s => s with { OverflowWrap = OverflowWrap.BreakWord }));
            stopwatch.Stop();

            Assert.Equal(text.Length, layout.Lines[^1].Range.End);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60), $"took {stopwatch.Elapsed}");
        }
    }
}
