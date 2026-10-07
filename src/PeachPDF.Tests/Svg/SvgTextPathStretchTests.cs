using PeachDrawing.Core;
using PeachPDF.Adapters;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// <c>&lt;textPath&gt;</c> <c>method="stretch"</c>, outlined glyphs painted in user space (paint servers measured against the whole
    /// text), the bounding box of a <c>&lt;text&gt;</c> for <c>objectBoundingBox</c> clipping, and the invisible text that keeps outlined
    /// text selectable. Asserted on the calls made to the canvas, not on PDF substrings.
    /// </summary>
    public class SvgTextPathStretchTests
    {
        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        private const string Gradient = """<linearGradient id="grad"><stop offset="0" stop-color="red"/><stop offset="1" stop-color="blue"/></linearGradient>""";

        // A semicircle of radius 60: tight enough that a glyph bent along it differs from the same glyph turned rigidly.
        private const string Arc = """<path id="p" d="M20,90 A60,60 0 0 1 140,90"/>""";

        private static (TestRecordingGraphics G, SvgDocument Doc) Render(string body, string defs = "")
        {
            var markup = $$"""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
                  <defs>{{defs}}</defs>
                  {{body}}
                </svg>
                """;
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new Rect(0, 0, 200, 100));
            return (g, document);
        }

        private static List<TestRecordingGraphics.DrawPathCall> Paths(TestRecordingGraphics g) => g.Log.OfType<TestRecordingGraphics.DrawPathCall>().ToList();

        [Theory]
        [InlineData("""<textPath href="#p" method="stretch">Hi</textPath>""", true)]
        [InlineData("""<textPath href="#p" method="align">Hi</textPath>""", false)]
        [InlineData("""<textPath href="#p" method="bogus">Hi</textPath>""", false)]
        [InlineData("""<textPath href="#p" spacing="auto">Hi</textPath>""", false)]
        [InlineData("""<textPath href="#p">Hi</textPath>""", false)]
        public void Method_IsParsed(string textPath, bool stretch)
        {
            var markup = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100"><defs>{Arc}</defs><text font-size="20">{textPath}</text></svg>""";
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var text = Assert.IsType<SvgTextElement>(Assert.Single(document.Children));
            var run = Assert.Single(text.Spans);

            Assert.Equal(stretch ? SvgTextPathMethod.Stretch : SvgTextPathMethod.Align, run.Method);
        }

        [Fact]
        public void Stretch_BendsTheGlyphOutline_AlignKeepsItRigid()
        {
            var align = Render($"""<text font-size="40" fill="url(#grad)"><textPath href="#p">H</textPath></text>""", Arc + Gradient);
            var stretch = Render($"""<text font-size="40" fill="url(#grad)"><textPath href="#p" method="stretch">H</textPath></text>""", Arc + Gradient);

            var rigid = Assert.Single(Paths(align.G));
            var bent = Assert.Single(Paths(stretch.G));

            // The test outline is a rectangle (4 corners); turned to the tangent it stays one, bent along the arc its edges are cut up.
            Assert.True(rigid.Points.Count <= 5, $"rigid {rigid.Points.Count}");
            Assert.True(bent.Points.Count > 20, $"bent {bent.Points.Count}");

            // A rectangle turned rigidly has two distinct edge directions; a bent one's top edge leaves the straight line between its ends.
            var top = bent.Points.Select(p => (p.X, p.Y)).Distinct().ToList();
            Assert.True(top.Count > 20);
        }

        [Fact]
        public void Stretch_GlyphStillReadsAlongThePath()
        {
            var stretch = Render($"""<text font-size="20" fill="url(#grad)"><textPath href="#p" method="stretch">HH</textPath></text>""", Arc + Gradient);
            var paths = Paths(stretch.G);

            Assert.Equal(2, paths.Count);

            // Every point of the glyph sits within reach of the arc's circle (radius 60 about (80,90), glyph height 20 plus slack).
            foreach (var point in paths.SelectMany(p => p.Points))
            {
                var distance = System.Math.Sqrt((point.X - 80) * (point.X - 80) + (point.Y - 90) * (point.Y - 90));
                Assert.InRange(distance, 60 - 1, 60 + 50);
            }
        }

        [Fact]
        public void UserSpaceGradient_OnCurvedTextPath_IsTheSameForEveryGlyph()
        {
            var gradient = """<linearGradient id="grad" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="200" y2="0"><stop offset="0" stop-color="red"/><stop offset="1" stop-color="blue"/></linearGradient>""";
            var (g, _) = Render("""<text font-size="20" fill="url(#grad)"><textPath href="#p">Hello</textPath></text>""", Arc + gradient);
            var paths = Paths(g);

            Assert.Equal(5, paths.Count);

            // The paint server is fixed in the text's user space, not carried round in each glyph's rotated frame.
            Assert.All(paths, p => Assert.Equal(paths[0].GradientStart, p.GradientStart));
            Assert.All(paths, p => Assert.Equal(paths[0].GradientEnd, p.GradientEnd));
            Assert.Equal(0, paths[0].GradientStart!.Value.X, 3);
            Assert.Equal(200, paths[0].GradientEnd!.Value.X, 3);
        }

        [Fact]
        public void BoundingBoxGradient_OnTextPath_SpansTheWholeText_NotEachGlyph()
        {
            var (g, _) = Render("""<text font-size="20" fill="url(#grad)"><textPath href="#p">Hello</textPath></text>""", Arc + Gradient);
            var paths = Paths(g);

            Assert.Equal(5, paths.Count);
            Assert.All(paths, p => Assert.Equal(paths[0].GradientStart, p.GradientStart));
            Assert.All(paths, p => Assert.Equal(paths[0].GradientEnd, p.GradientEnd));

            // Five 12-wide glyphs along the arc cover far more than one glyph's own box.
            var span = paths[0].GradientEnd!.Value.X - paths[0].GradientStart!.Value.X;
            Assert.True(span > 30, $"gradient spanned {span}");
        }

        [Fact]
        public void OutlinedTextPath_AddsInvisibleTextForEachGlyph_SoItStaysSelectable()
        {
            var (g, _) = Render("""<text font-size="20" fill="url(#grad)"><textPath href="#p">Hi</textPath></text>""", Arc + Gradient);

            Assert.Empty(g.DrawStringCalls);
            Assert.Equal(["H", "i"], g.InvisibleStringCalls.Select(c => c.Text));
        }

        [Fact]
        public void StretchedText_AddsInvisibleText()
        {
            var (g, _) = Render("""<text font-size="20"><textPath href="#p" method="stretch">Hi</textPath></text>""", Arc);

            Assert.Equal(2, Paths(g).Count);
            Assert.Equal(["H", "i"], g.InvisibleStringCalls.Select(c => c.Text));
        }

        [Fact]
        public void OutlinedStraightText_AddsInvisibleTextAfterThePaint()
        {
            var (g, _) = Render("""<text x="10" y="50" font-size="40" fill="url(#grad)">Hi</text>""", Gradient);

            Assert.Single(Paths(g));
            var invisible = Assert.Single(g.InvisibleStringCalls);
            Assert.Equal("Hi", invisible.Text);
        }

        [Fact]
        public void SolidText_AddsNoInvisibleText()
        {
            var (g, _) = Render("""<text x="10" y="50" font-size="40" fill="rgb(1,2,3)">Hi</text>""");

            Assert.Single(g.DrawStringCalls);
            Assert.Empty(g.InvisibleStringCalls);
        }

        [Fact]
        public void ObjectBoundingBoxClipPath_OnText_UsesTheTextBounds()
        {
            var clip = """<clipPath id="c" clipPathUnits="objectBoundingBox"><rect x="0" y="0" width="1" height="0.5"/></clipPath>""";
            var (g, _) = Render("""<text x="10" y="50" font-size="40" clip-path="url(#c)">Hi</text>""", clip);

            // "Hi" is 48 wide (the recorder's metrics); the clip covers the text's own box, not a unit box at the origin.
            var clipPath = Assert.Single(g.ClipPaths);
            var rect = Rect.FromLTRB(clipPath.Points.Min(p => p.X), clipPath.Points.Min(p => p.Y), clipPath.Points.Max(p => p.X), clipPath.Points.Max(p => p.Y));
            Assert.Equal(10, rect.X, 1);
            Assert.Equal(48, rect.Width, 1);
        }

        [Fact]
        public void ObjectBoundingBoxClipPath_OnTextPath_UsesTheLaidOutGlyphs()
        {
            var clip = """<clipPath id="c" clipPathUnits="objectBoundingBox"><rect x="0" y="0" width="1" height="1"/></clipPath>""";
            var (g, _) = Render("""<text font-size="20" clip-path="url(#c)"><textPath href="#p">Hello</textPath></text>""", Arc + clip);

            var clipPath = Assert.Single(g.ClipPaths);
            var rect = Rect.FromLTRB(clipPath.Points.Min(p => p.X), clipPath.Points.Min(p => p.Y), clipPath.Points.Max(p => p.X), clipPath.Points.Max(p => p.Y));

            // Along the arc the glyphs rise and fall; the box is nowhere near a single line of text high.
            Assert.True(rect.Height > 20, $"height {rect.Height}");
            Assert.InRange(rect.X, -20, 80);
        }
    }
}
