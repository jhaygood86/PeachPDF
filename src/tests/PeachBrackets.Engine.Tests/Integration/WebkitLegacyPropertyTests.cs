using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// The WebKit-era properties with no standard spelling: <c>-webkit-text-fill-color</c>, <c>-webkit-text-security</c>,
    /// <c>-webkit-box-reflect</c> and (with its standard name) <c>initial-letter</c>, asserted on layout and on the calls the painter makes.
    /// </summary>
    public class WebkitLegacyPropertyTests
    {
        private static async Task<(CssBox Root, PeachBrackets.Html.HtmlContainerInt Container)> LayoutAsync(string body) =>
            await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body));

        private static async Task<TestRecordingGraphics> PaintAsync(string body, TestRecordingGraphics? g = null)
        {
            var (_, container) = await LayoutAsync(body);
            g ??= new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);
            return g;
        }

        // ---- -webkit-text-fill-color --------------------------------------------------------------

        [Fact]
        public async Task TextFillColor_PaintsTheGlyphsInThatColour_NotColor()
        {
            var g = await PaintAsync("<p style=\"margin:0;font:20pt Arial;color:#0000ff;-webkit-text-fill-color:#ff0000\">Hi</p>");

            var word = g.DrawStringCalls.First(c => c.Text == "Hi");
            Assert.Equal(PaintColor.FromArgb(255, 255, 0, 0), word.PaintColor);
        }

        [Fact]
        public async Task TextFillColor_DefaultsToCurrentColor_AndInherits()
        {
            var g = await PaintAsync("<div style=\"-webkit-text-fill-color:#00ff00\"><p style=\"margin:0;font:20pt Arial;color:#0000ff\">Hi</p></div>");
            Assert.Equal(PaintColor.FromArgb(255, 0, 255, 0), g.DrawStringCalls.First(c => c.Text == "Hi").PaintColor);

            var plain = await PaintAsync("<p style=\"margin:0;font:20pt Arial;color:#0000ff\">Hi</p>");
            Assert.Equal(PaintColor.FromArgb(255, 0, 0, 255), plain.DrawStringCalls.First(c => c.Text == "Hi").PaintColor);
        }

        // ---- -webkit-text-security ----------------------------------------------------------------

        [Theory]
        [InlineData("disc", "•••")]
        [InlineData("circle", "◦◦◦")]
        [InlineData("square", "▪▪▪")]
        [InlineData("none", "abc")]
        public async Task TextSecurity_ReplacesEachCharacterWithTheBullet(string value, string expected)
        {
            var g = await PaintAsync($"<p style=\"margin:0;font:20pt Arial;-webkit-text-security:{value}\">abc</p>");

            Assert.Contains(g.DrawStringCalls, c => c.Text == expected);
        }

        [Fact]
        public async Task TextSecurity_KeepsWhitespaceSoWordsStillBreak_AndInherits()
        {
            var g = await PaintAsync("<div style=\"-webkit-text-security:disc\"><p style=\"margin:0;font:20pt Arial\">ab cd</p></div>");

            var words = g.WordDrawStringCalls.Select(c => c.Text).ToArray();
            Assert.Equal(new[] { "••", "••" }, words);
        }

        // ---- -webkit-box-reflect ------------------------------------------------------------------

        [Theory]
        [InlineData("none", true)]
        [InlineData("below", true)]
        [InlineData("above 4px", true)]
        [InlineData("right 2px linear-gradient(transparent, white)", true)]
        [InlineData("sideways", false)]
        [InlineData("below 4px red", false)]
        [InlineData("", false)]
        public void BoxReflectGrammar_AcceptsTheDocumentedForms(string value, bool valid) =>
            Assert.Equal(valid, BoxReflectGrammar.TryParse(value, out _, out _, out _, out _));

        [Fact]
        public void BoxReflectGrammar_ReadsDirectionOffsetAndImage()
        {
            Assert.True(BoxReflectGrammar.TryParse("left 3px linear-gradient(red, blue)", out var none, out var direction, out var offset, out var image));
            Assert.False(none);
            Assert.Equal(BoxReflectDirection.Left, direction);
            Assert.Equal("3px", offset);
            Assert.Equal("linear-gradient(red, blue)", image);
        }

        [Fact]
        public async Task BoxReflect_PaintsTheBoxAgainMirroredBelowIt_WithoutTakingSpace()
        {
            var plain = await PaintAsync("<div id='d' style=\"width:100pt;height:50pt;background:#ff0000\"></div>");
            var g = await PaintAsync("<div id='d' style=\"width:100pt;height:50pt;background:#ff0000;-webkit-box-reflect:below 10pt\"></div>");

            Assert.DoesNotContain(plain.Log, c => c is TestRecordingGraphics.PushTransformCall);

            // The mirror about the bottom edge: y' = 2*bottom + offset - y.
            var push = Assert.Single(g.Log.OfType<TestRecordingGraphics.PushTransformCall>());
            Assert.Equal(1f, push.Matrix.M11);
            Assert.Equal(-1f, push.Matrix.M22);

            var (root, _) = await LayoutAsync("<div id='d' style=\"width:100pt;height:50pt;-webkit-box-reflect:below 10pt\"></div><div id='e'>x</div>");
            var d = LayoutHarness.FindById(root, "d")!;
            var e = LayoutHarness.FindById(root, "e")!;
            Assert.Equal(d.ActualBottom, e.Location.Y, 3);
        }

        // ---- initial-letter -----------------------------------------------------------------------

        [Theory]
        [InlineData("normal", true, 0, 0)]
        [InlineData("3", true, 3, 3)]
        [InlineData("3 2", true, 3, 2)]
        [InlineData("2.5", true, 2.5, 2)]
        [InlineData("3 raise", true, 3, 1)]
        [InlineData("3 drop", true, 3, 3)]
        [InlineData("0.5", false, 0, 0)]
        [InlineData("3 0", false, 0, 0)]
        [InlineData("drop", false, 0, 0)]
        [InlineData("3 2 1", false, 0, 0)]
        public void InitialLetterGrammar_ParsesSizeAndSink(string value, bool valid, double size, int sink)
        {
            Assert.Equal(valid, InitialLetterGrammar.TryParse(value, out var parsedSize, out var parsedSink));
            if (!valid) return;
            Assert.Equal(size, parsedSize);
            Assert.Equal(sink, parsedSink);
        }

        private const string Para =
            "<style>p { margin:0; font:12pt/18pt Arial; width:300pt } p::first-letter { initial-letter: 3 }</style>" +
            "<p id='p'>Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.</p>";

        [Fact]
        public async Task InitialLetter_FloatsTheLetter_AndSizesItsFontToSpanTheLines()
        {
            var (root, _) = await LayoutAsync(Para);
            var p = LayoutHarness.FindById(root, "p")!;
            var letter = p.Boxes.Single(b => b.IsFirstLetterPseudoElement);

            Assert.True(letter.IsFloated);
            // Cap height of 3 lines: (3-1)*18 + 0.716*12 ~ 44.6pt => a font of roughly 62pt for Arial's 0.716 cap height.
            Assert.InRange(letter.ActualFont.Size, 2.5 * p.ActualFont.Size, 6 * p.ActualFont.Size);
        }

        [Fact]
        public async Task InitialLetter_NarrowsExactlyTheSinkLines_ThenTextReturnsToTheLeftEdge()
        {
            var (root, container) = await LayoutAsync(Para);
            var p = LayoutHarness.FindById(root, "p")!;
            var letter = p.Boxes.Single(b => b.IsFirstLetterPseudoElement);

            var words = p.Boxes.Where(b => !b.IsFirstLetterPseudoElement).SelectMany(b => b.Words).ToList();
            var lineTops = words.Select(w => w.Top).Distinct().OrderBy(t => t).ToList();
            Assert.True(lineTops.Count >= 5);

            var left = p.ClientLeft;
            var firstOfLine = lineTops.Select(t => words.Where(w => w.Top == t).Min(w => w.Left)).ToList();

            // Lines 1-3 start to the right of the letter, line 4 is back at the content edge.
            Assert.True(firstOfLine[0] >= letter.ActualRight - 0.5);
            Assert.True(firstOfLine[1] >= letter.ActualRight - 0.5);
            Assert.True(firstOfLine[2] >= letter.ActualRight - 0.5);
            Assert.Equal(left, firstOfLine[3], 1);
            Assert.NotNull(container);
        }

        [Fact]
        public async Task InitialLetter_SinkTwoNarrowsTwoLines()
        {
            var (root, _) = await LayoutAsync(Para.Replace("initial-letter: 3", "initial-letter: 3 2"));
            var p = LayoutHarness.FindById(root, "p")!;
            var letter = p.Boxes.Single(b => b.IsFirstLetterPseudoElement);
            var words = p.Boxes.Where(b => !b.IsFirstLetterPseudoElement).SelectMany(b => b.Words).ToList();
            var lineTops = words.Select(w => w.Top).Distinct().OrderBy(t => t).ToList();
            var firstOfLine = lineTops.Select(t => words.Where(w => w.Top == t).Min(w => w.Left)).ToList();

            Assert.True(firstOfLine[1] >= letter.ActualRight - 0.5);
            Assert.Equal(p.ClientLeft, firstOfLine[2], 1);
        }

        [Fact]
        public async Task InitialLetter_Normal_LeavesTheLetterInline()
        {
            var (root, _) = await LayoutAsync(Para.Replace("initial-letter: 3", "initial-letter: normal"));
            var letter = LayoutHarness.FindById(root, "p")!.Boxes.Single(b => b.IsFirstLetterPseudoElement);

            Assert.False(letter.IsFloated);
        }

        [Fact]
        public async Task InitialLetter_WebkitPrefix_IsTheSameProperty()
        {
            var (root, _) = await LayoutAsync(Para.Replace("initial-letter: 3", "-webkit-initial-letter: 3"));
            var letter = LayoutHarness.FindById(root, "p")!.Boxes.Single(b => b.IsFirstLetterPseudoElement);

            Assert.True(letter.IsFloated);
        }
    }
}
