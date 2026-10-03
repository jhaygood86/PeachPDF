using PeachDrawing.Core;
using PeachPDF.Adapters;
using PeachPDF.PdfSharpCore;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>Auto-wrapped SVG text (SVG 2 §11.7): <c>inline-size</c>, <c>shape-inside</c>/<c>shape-subtract</c> and the CSS text properties that act on its line boxes.</summary>
    public class SvgTextWrappingTests
    {
        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        private static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static TestRecordingGraphics Render(string textAttrs, string content, double x = 10, double y = 50)
        {
            var markup = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 300"><text x="{Num(x)}" y="{Num(y)}" font-size="20" {textAttrs}>{content}</text></svg>""";
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new Rect(0, 0, 400, 300));
            return g;
        }

        /// <summary>The width of <paramref name="text"/> set on one unwrapped line.</summary>
        private static double WidthOf(string text) => Render("", text).DrawStringCalls.Sum(c => c.Size.Width);

        /// <summary>The painted lines: every draw call, grouped by baseline (top of the draw box), left to right, each with its text.</summary>
        private static (double Y, double Left, double Right, string Text)[] Lines(TestRecordingGraphics g) =>
            g.DrawStringCalls
                .GroupBy(c => c.PaintPoint.Y)
                .OrderBy(group => group.Key)
                .Select(group => (group.Key, group.Min(c => c.PaintPoint.X), group.Max(c => c.PaintPoint.X + c.Size.Width), string.Concat(group.OrderBy(c => c.PaintPoint.X).Select(c => c.Text))))
                .ToArray();

        private const string FourWords = "word word word word";

        [Fact]
        public void InlineSize_BreaksTheTextIntoLinesThatFit()
        {
            var inline = WidthOf("word word") + 2;
            var lines = Lines(Render($"""inline-size="{Num(inline)}" """, FourWords));

            Assert.Equal(["word word", "word word"], lines.Select(l => l.Text.TrimEnd()).ToArray());
            Assert.All(lines, l => Assert.Equal(10, l.Left, 3));
            Assert.All(lines, l => Assert.True(l.Right - 10 <= inline + 0.01));
            Assert.True(lines[1].Y > lines[0].Y);
        }

        [Fact]
        public void InlineSize_FirstBaselineIsTheTextY()
        {
            var unwrapped = Render("", "word");
            var wrapped = Render("""inline-size="300" """, "word");

            Assert.Equal(unwrapped.DrawStringCalls[0].PaintPoint.Y, wrapped.DrawStringCalls[0].PaintPoint.Y, 3);
        }

        [Fact]
        public void NoInlineSize_StaysOneUnwrappedLine()
        {
            var g = Render("", FourWords);

            Assert.Single(g.DrawStringCalls);
            Assert.Equal(FourWords, g.DrawStringCalls[0].Text);
        }

        [Fact]
        public void InlineSize_AutoMeansNoWrapping()
        {
            Assert.Single(Render("""inline-size="auto" """, FourWords).DrawStringCalls);
        }

        [Fact]
        public void InlineSize_AWordWiderThanTheBoxOverflowsOnItsOwnLine()
        {
            var lines = Lines(Render("""inline-size="5" """, "wide words"));

            Assert.Equal(["wide", "words"], lines.Select(l => l.Text.TrimEnd()).ToArray());
        }

        [Fact]
        public void LineHeight_Number_ScalesTheFontSize()
        {
            var lines = Lines(Render($"""inline-size="{Num(WidthOf("word") + 1)}" line-height="1.5" """, "word word word"));

            Assert.Equal(3, lines.Length);
            Assert.Equal(30, lines[1].Y - lines[0].Y, 3);
            Assert.Equal(30, lines[2].Y - lines[1].Y, 3);
        }

        [Fact]
        public void LineHeight_Length_IsAbsolute()
        {
            var lines = Lines(Render($"""inline-size="{Num(WidthOf("word") + 1)}" style="line-height: 40px" """, "word word"));

            Assert.Equal(40, lines[1].Y - lines[0].Y, 3);
        }

        [Fact]
        public void LineHeight_Normal_IsTheFontsOwnLineHeight()
        {
            var lines = Lines(Render($"""inline-size="{Num(WidthOf("word") + 1)}" """, "word word"));

            Assert.True(lines[1].Y - lines[0].Y > 20);
            Assert.True(lines[1].Y - lines[0].Y < 30);
        }

        [Fact]
        public void TextIndent_PushesOnlyTheFirstLine()
        {
            var lines = Lines(Render($"""inline-size="{Num(WidthOf("word") + 30)}" text-indent="15px" """, "word word word"));

            Assert.Equal(25, lines[0].Left, 3);
            Assert.Equal(10, lines[1].Left, 3);
            Assert.Equal(10, lines[2].Left, 3);
        }

        [Fact]
        public void TextIndent_ShrinksTheFirstLineSoAWordWrapsEarlier()
        {
            var inline = WidthOf("word word") + 2;
            Assert.Equal(2, Lines(Render($"""inline-size="{Num(inline)}" """, "word word word")).Length);

            var indented = Lines(Render($"""inline-size="{Num(inline)}" text-indent="40px" """, "word word word"));
            Assert.Equal(["word", "word word"], indented.Select(l => l.Text.TrimEnd()).ToArray());
        }

        [Fact]
        public void TextIndent_PercentageIsOfTheInlineSize()
        {
            var lines = Lines(Render("""inline-size="200" text-indent="10%" """, "word"));

            Assert.Equal(30, lines[0].Left, 3);
        }

        [Theory]
        [InlineData("start", 0.0)]
        [InlineData("left", 0.0)]
        [InlineData("center", 0.5)]
        [InlineData("end", 1.0)]
        [InlineData("right", 1.0)]
        public void TextAlign_PlacesEachLineInTheBox(string align, double fraction)
        {
            var word = WidthOf("word");
            var lines = Lines(Render($"""inline-size="200" text-align="{align}" """, "word"));

            Assert.Equal(10 + (200 - word) * fraction, lines[0].Left, 2);
        }

        [Theory]
        [InlineData("start", 0.0)]
        [InlineData("middle", 0.5)]
        [InlineData("end", 1.0)]
        public void TextAnchor_StandsInForTextAlign(string anchor, double fraction)
        {
            var word = WidthOf("word");
            var lines = Lines(Render($"""inline-size="200" text-anchor="{anchor}" """, "word"));

            Assert.Equal(10 + (200 - word) * fraction, lines[0].Left, 2);
        }

        [Fact]
        public void TextAlign_WinsOverTextAnchor()
        {
            var lines = Lines(Render("""inline-size="200" text-align="start" text-anchor="end" """, "word"));

            Assert.Equal(10, lines[0].Left, 3);
        }

        [Fact]
        public void TextAlign_JustifyFillsEveryLineButTheLast()
        {
            var inline = WidthOf("aa bb cc") + 6;
            var g = Render($"""inline-size="{Num(inline)}" text-align="justify" """, "aa bb cc dd ee ff gg");
            var lines = Lines(g);

            Assert.True(lines.Length >= 2);
            for (var i = 0; i < lines.Length - 1; i++)
                Assert.Equal(10 + inline, lines[i].Right, 2);

            Assert.True(lines[^1].Right < 10 + inline - 1);
            Assert.Equal(10, lines[^1].Left, 3);
        }

        [Fact]
        public void TextAlign_JustifyOpensTheGapsBetweenWords()
        {
            var inline = WidthOf("aa bb cc") + 6;
            var justified = Lines(Render($"""inline-size="{Num(inline)}" text-align="justify" """, "aa bb cc dd ee ff gg"));
            var ragged = Lines(Render($"""inline-size="{Num(inline)}" """, "aa bb cc dd ee ff gg"));

            Assert.Equal(ragged[0].Text.TrimEnd(), justified[0].Text.TrimEnd());
            Assert.True(justified[0].Right > ragged[0].Right);
        }

        [Fact]
        public void Pre_BreaksAtLineFeedsAndNeverWraps()
        {
            var lines = Lines(Render("""inline-size="5" white-space="pre" """, "one two\nthree"));

            Assert.Equal(["one two", "three"], lines.Select(l => l.Text).ToArray());
        }

        [Fact]
        public void PreWrap_KeepsSpacesAndBreaksAtLineFeedsAndWraps()
        {
            var inline = WidthOf("word") + 1;
            var lines = Lines(Render($"""inline-size="{Num(inline)}" white-space="pre-wrap" """, "a  b\nword word"));

            Assert.Equal(["a  b", "word", "word"], lines.Select(l => l.Text.TrimEnd()).ToArray());
        }

        [Fact]
        public void PreLine_CollapsesSpacesButKeepsLineFeeds()
        {
            var lines = Lines(Render("""inline-size="300" white-space="pre-line" """, "a   b  \n   c"));

            Assert.Equal(["a b", "c"], lines.Select(l => l.Text.TrimEnd()).ToArray());
        }

        [Fact]
        public void Normal_TurnsLineFeedsIntoSpaces()
        {
            var lines = Lines(Render("""inline-size="300" """, "a\nb"));

            Assert.Single(lines);
            Assert.Equal("a b", lines[0].Text);
        }

        [Fact]
        public void NoWrap_NeverBreaksAtASpace()
        {
            var lines = Lines(Render("""inline-size="5" white-space="nowrap" """, FourWords));

            Assert.Single(lines);
        }

        [Fact]
        public void LineFeeds_AreSpacesWhenTheTextIsNotWrapped()
        {
            var g = Render("""white-space="pre" """, "a\nb");

            Assert.Single(g.DrawStringCalls);
            Assert.Equal("a b", g.DrawStringCalls[0].Text);
        }

        [Fact]
        public void Pre_ATrailingLineFeedAddsNoEmptyLine()
        {
            Assert.Single(Lines(Render("""inline-size="300" white-space="pre" """, "only\n")));
        }

        [Fact]
        public void Pre_ABlankLineKeepsItsHeight()
        {
            var lines = Lines(Render("""inline-size="300" white-space="pre" line-height="30px" """, "a\n\nb"));

            Assert.Equal(2, lines.Length);
            Assert.Equal(60, lines[1].Y - lines[0].Y, 3);
        }

        [Fact]
        public void SoftHyphen_BreaksTheWordWithAHyphen()
        {
            var inline = WidthOf("super-") + 1;
            var lines = Lines(Render($"""inline-size="{Num(inline)}" """, "super&#xAD;cali"));

            Assert.Equal(["super-", "cali"], lines.Select(l => l.Text).ToArray());
        }

        [Fact]
        public void SoftHyphen_IsInvisibleWhenTheWordDoesNotBreakThere()
        {
            var lines = Lines(Render("""inline-size="300" """, "super&#xAD;cali"));

            Assert.Equal(["supercali"], lines.Select(l => l.Text).ToArray());
        }

        [Fact]
        public void HyphensNone_IgnoresTheSoftHyphen()
        {
            var inline = WidthOf("super-") + 1;
            var lines = Lines(Render($"""inline-size="{Num(inline)}" hyphens="none" """, "super&#xAD;cali"));

            Assert.Single(lines);
        }

        [Fact]
        public void HyphensAuto_BreaksAWordAtAHyphenationPoint()
        {
            var word = "internationalization";
            var lines = Lines(Render($"""inline-size="{Num(WidthOf(word) * 0.6)}" hyphens="auto" lang="en" """, word));

            Assert.True(lines.Length >= 2);
            Assert.EndsWith("-", lines[0].Text);
            Assert.Equal(word, string.Concat(lines.Select(l => l.Text.TrimEnd('-'))));
        }

        [Fact]
        public void HyphensManual_DoesNotHyphenateAnOrdinaryWord()
        {
            var word = "internationalization";
            var lines = Lines(Render($"""inline-size="{Num(WidthOf(word) * 0.6)}" lang="en" """, word));

            Assert.Single(lines);
        }

        [Fact]
        public void TextLength_HasNoEffectOnWrappedText()
        {
            var plain = Lines(Render("""inline-size="150" """, FourWords));
            var adjusted = Lines(Render("""inline-size="150" textLength="900" """, FourWords));

            Assert.Equal(plain.Select(l => l.Text), adjusted.Select(l => l.Text));
            Assert.Equal(plain.Select(l => l.Left), adjusted.Select(l => l.Left));
            Assert.Equal(plain.Select(l => l.Right), adjusted.Select(l => l.Right));
        }

        [Fact]
        public void PerCharacterPositions_AreIgnoredInWrappedText()
        {
            var lines = Lines(Render("""inline-size="300" dx="50" dy="30" """, "word"));

            Assert.Equal(10, lines[0].Left, 3);
            Assert.Equal(Lines(Render("""inline-size="300" """, "word"))[0].Y, lines[0].Y, 3);
        }

        [Fact]
        public void RightToLeft_TheBoxRunsLeftwardFromX()
        {
            var inline = 120.0;
            var lines = Lines(Render($"""inline-size="{Num(inline)}" direction="rtl" """, "אבג אבג אבג אבג אבג", x: 200));

            Assert.True(lines.Length >= 2);
            Assert.All(lines, l => Assert.Equal(200, l.Right, 1));
            Assert.All(lines, l => Assert.True(l.Left >= 200 - inline - 0.01));
        }

        [Fact]
        public void MixedDirections_AreReorderedOnEachLine()
        {
            var lines = Lines(Render("""inline-size="300" """, "abc אבג"));

            Assert.Single(lines);
            Assert.Equal("abc גבא", lines[0].Text);
        }

        [Fact]
        public void MixedDirections_EachLineIsReorderedOnItsOwn()
        {
            var inline = WidthOf("abc") + 1;
            var lines = Lines(Render($"""inline-size="{Num(inline)}" """, "abc אבג abc"));

            Assert.Equal(["abc", "גבא", "abc"], lines.Select(l => l.Text.Trim()).ToArray());
        }

        [Fact]
        public void LineHeight_TheTallestRunSetsTheLinesBox()
        {
            // Two lines of one font: the baselines are as far apart as half of each line's height added together.
            var lines = Lines(Render($"""inline-size="{Num(WidthOf("word") + 1)}" line-height="30px" """, "word <tspan style=\"line-height: 60px\">word</tspan>"));

            Assert.Equal(45, lines[1].Y - lines[0].Y, 3);
        }

        [Fact]
        public void ShapeInside_Rectangle_WrapsLikeAnInlineSizeBox()
        {
            var inline = WidthOf("word word") + 2;
            var shape = Lines(Render($"""shape-inside="polygon(10px 0, {Num(10 + inline)}px 0, {Num(10 + inline)}px 280px, 10px 280px)" """, FourWords));
            var box = Lines(Render($"""inline-size="{Num(inline)}" """, FourWords));

            Assert.Equal(box.Select(l => l.Text), shape.Select(l => l.Text));
            Assert.Equal(box.Select(l => l.Left), shape.Select(l => l.Left));
        }

        [Fact]
        public void ShapeInside_Triangle_LinesGetWiderDownTheShape()
        {
            var words = string.Join(' ', Enumerable.Repeat("aa", 60));
            var lines = Lines(Render("""shape-inside="polygon(200px 0, 380px 290px, 20px 290px)" text-align="center" """, words, x: 0, y: 30));

            Assert.True(lines.Length >= 3);
            var widths = lines.Select(l => l.Right - l.Left).ToArray();
            Assert.True(widths[^1] > widths[0]);
            Assert.All(lines, l => Assert.True(l.Left >= 20 - 0.01 && l.Right <= 380 + 0.01));
        }

        [Fact]
        public void ShapeInside_Circle_KeepsEveryLineInsideTheCircle()
        {
            var words = string.Join(' ', Enumerable.Repeat("aaa", 80));
            var lines = Lines(Render("""shape-inside="circle(120px at 200px 150px)" """, words, x: 0, y: 50));

            Assert.True(lines.Length >= 3);
            var fontHeight = Render("", "x").DrawStringCalls[0].Size.Height;
            foreach (var line in lines)
            {
                var top = line.Y;
                var bottom = line.Y + fontHeight;
                var worst = System.Math.Max(System.Math.Abs(top - 150), System.Math.Abs(bottom - 150));
                var halfChord = System.Math.Sqrt(System.Math.Max(0, 120 * 120 - worst * worst));
                Assert.True(line.Left >= 200 - halfChord - 1, $"left {line.Left} outside chord {halfChord}");
                Assert.True(line.Right <= 200 + halfChord + 1, $"right {line.Right} outside chord {halfChord}");
            }
        }

        [Fact]
        public void ShapeInside_TextThatDoesNotFitIsNotPainted()
        {
            var words = string.Join(' ', Enumerable.Repeat("word", 40));
            var g = Render("""shape-inside="polygon(0px 0, 100px 0, 100px 80px, 0px 80px)" """, words, x: 0, y: 20);

            var painted = string.Concat(g.DrawStringCalls.Select(c => c.Text));
            Assert.True(painted.Length > 0);
            Assert.True(painted.Length < words.Length / 2);
        }

        [Fact]
        public void ShapeInside_ASetShapeTakesThePlaceOfInlineSize()
        {
            var lines = Lines(Render("""inline-size="10" shape-inside="polygon(0px 0, 300px 0, 300px 280px, 0px 280px)" """, "word word", x: 0));

            Assert.Single(lines);
        }

        [Fact]
        public void ShapeSubtract_SplitsTheLineAroundTheCutOut()
        {
            var g = Render("""inline-size="200" shape-subtract="polygon(70px 0, 120px 0, 120px 280px, 70px 280px)" """, string.Join(' ', Enumerable.Repeat("w", 12)));

            var xs = g.DrawStringCalls.Select(c => (Left: c.PaintPoint.X, Right: c.PaintPoint.X + c.Size.Width)).ToArray();
            Assert.Contains(xs, x => x.Right <= 70 + 0.01);
            Assert.Contains(xs, x => x.Left >= 120 - 0.01);
            Assert.DoesNotContain(xs, x => x.Left < 120 - 0.01 && x.Right > 70 + 0.01);
        }

        [Fact]
        public void Wrapped_Vertical_FallsBackToUnwrappedText()
        {
            var g = Render("""inline-size="30" writing-mode="vertical-rl" """, "ab cd");

            Assert.NotEmpty(g.DrawStringCalls);
            Assert.All(g.DrawStringCalls, c => Assert.Equal(g.DrawStringCalls[0].PaintPoint.X, c.PaintPoint.X, 3));
        }

        [Fact]
        public void Decorations_FollowEachLine()
        {
            var plain = Render($"""inline-size="{Num(WidthOf("word") + 1)}" """, "word word");
            var underlined = Render($"""inline-size="{Num(WidthOf("word") + 1)}" text-decoration="underline" """, "word word");

            Assert.Equal(plain.DrawStringCalls.Count, underlined.DrawStringCalls.Count);
            Assert.True(underlined.Log.Count > plain.Log.Count);
        }

        private static async Task<string> PdfContent(string svgTextAttrs)
        {
            var html = $"""<!DOCTYPE html><html><body style="margin:0"><svg xmlns="http://www.w3.org/2000/svg" width="300" height="200" viewBox="0 0 300 200"><text x="10" y="30" font-size="20" {svgTextAttrs}>aa bb cc dd ee ff gg hh</text></svg></body></html>""";
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var document = await new PdfGenerator().GeneratePdf(html, config);
            using var stream = new MemoryStream();
            document.Save(stream);
            return Encoding.Latin1.GetString(stream.ToArray());
        }

        [Fact]
        public async Task WrappedText_ReachesThePdfOnSeveralLines()
        {
            var unwrapped = await PdfContent("");
            var wrapped = await PdfContent("""inline-size="60" """);
            var faded = await PdfContent("""inline-size="60" opacity="0.5" """);

            static int Lines(string pdf) => System.Text.RegularExpressions.Regex.Matches(pdf, @"\sT[jJ]\s").Count;
            Assert.True(Lines(wrapped) > Lines(unwrapped));
            Assert.True(Lines(faded) >= Lines(wrapped));
        }
    }
}
