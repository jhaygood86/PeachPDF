using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Call-adjacency coverage for gradient/pattern <c>fill</c> and <c>stroke</c> on SVG
    /// <c>&lt;text&gt;</c> (issue #187). Renders through <see cref="SvgRenderer.RenderInto"/> into a
    /// <see cref="TestRecordingGraphics"/> and asserts the ordered sequence of paint calls: outlined
    /// text goes through <c>DrawPath</c> (fill then stroke), while plain solid text keeps the fast
    /// <c>DrawString</c> path.
    /// </summary>
    public class SvgTextOutlineTests
    {
        // PixelsPerPoint=1 so the resolved font Size matches the SVG user-unit font-size, keeping glyph
        // advances (used for textPath placement/drop-off) in the same 1:1 units as the path lengths.
        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        private static TestRecordingGraphics Render(string body, string defs = "")
        {
            var markup = $$"""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
                  <defs>{{defs}}</defs>
                  {{body}}
                </svg>
                """;
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new RRect(0, 0, 200, 100));
            return g;
        }

        private static List<TestRecordingGraphics.DrawPathCall> PathCalls(TestRecordingGraphics g)
            => g.Log.OfType<TestRecordingGraphics.DrawPathCall>().ToList();

        [Fact]
        public void GradientFillText_EmitsPathFill_AndNoTextShow()
        {
            var g = Render(
                """<text x="10" y="50" font-size="40" fill="url(#grad)">Hi</text>""",
                """<linearGradient id="grad"><stop offset="0" stop-color="red"/><stop offset="1" stop-color="blue"/></linearGradient>""");

            // A gradient fill can't be a single-color text show - it must be a filled outline path.
            Assert.Empty(g.DrawStringCalls);
            var paths = PathCalls(g);
            Assert.Single(paths);
            // The gradient brush carries its first stop's color (red).
            Assert.Equal(RColor.FromArgb(255, 255, 0, 0), paths[0].Color);
        }

        [Fact]
        public void StrokedText_EmitsFillThenStroke_InThatOrder()
        {
            var g = Render(
                """<text x="10" y="50" font-size="40" fill="rgb(0,128,0)" stroke="rgb(0,0,255)" stroke-width="2">Hi</text>""");

            // Stroke forces the outline path even though the fill is solid: SVG paint order is fill
            // first, then stroke.
            Assert.Empty(g.DrawStringCalls);
            var paths = PathCalls(g);
            Assert.Equal(2, paths.Count);
            Assert.Equal(RColor.FromArgb(255, 0, 128, 0), paths[0].Color);   // fill
            Assert.Equal(RColor.FromArgb(255, 0, 0, 255), paths[1].Color);   // stroke
        }

        [Fact]
        public void SolidText_UsesFastDrawString_NoOutlinePath()
        {
            var g = Render("""<text x="10" y="50" font-size="40" fill="rgb(10,20,30)">Hi</text>""");

            // Plain solid, non-stroked text keeps the selectable DrawString fast path.
            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("Hi", draw.Text);
            Assert.Equal(RColor.FromArgb(255, 10, 20, 30), draw.Color);
            Assert.Empty(PathCalls(g));
        }

        [Fact]
        public void GradientFillWithStroke_FillsWithGradientThenStrokes()
        {
            var g = Render(
                """<text x="10" y="50" font-size="40" fill="url(#grad)" stroke="rgb(0,0,0)" stroke-width="1">Hi</text>""",
                """<linearGradient id="grad"><stop offset="0" stop-color="lime"/><stop offset="1" stop-color="navy"/></linearGradient>""");

            Assert.Empty(g.DrawStringCalls);
            var paths = PathCalls(g);
            Assert.Equal(2, paths.Count);
            Assert.Equal(RColor.FromArgb(255, 0, 0, 0), paths[1].Color); // stroke last
        }

        [Fact]
        public void TextPath_SolidFill_DrawsOneGlyphPerCharacterAlongThePath()
        {
            // Path is 180 long; at font-size 40 each glyph advances 40*0.6=24 (TestRecordingGraphics
            // metrics), so "Path" (4 glyphs, midpoints 12/36/60/84) all fit.
            var g = Render(
                """<text font-size="40"><textPath href="#p">Path</textPath></text>""",
                """<path id="p" d="M10,50 L190,50"/>""");

            Assert.Equal(4, g.DrawStringCalls.Count);
            Assert.All(g.DrawStringCalls, c => Assert.Equal(1, c.Text.Length)); // one glyph per placement
        }

        [Fact]
        public void TextPath_GlyphsPastThePathEnd_AreDropped()
        {
            // A 30-long path: only the first glyph's midpoint (12) fits; the rest (36, 60, ...) fall off.
            var g = Render(
                """<text font-size="40"><textPath href="#p">Path</textPath></text>""",
                """<path id="p" d="M10,50 L40,50"/>""");

            Assert.Single(g.DrawStringCalls);
        }

        [Fact]
        public void TextPath_GradientFill_OutlinesEachGlyph_NoTextShow()
        {
            var g = Render(
                """<text font-size="40"><textPath href="#p" fill="url(#grad)">Hi</textPath></text>""",
                """<path id="p" d="M10,50 L190,50"/><linearGradient id="grad"><stop offset="0" stop-color="red"/><stop offset="1" stop-color="blue"/></linearGradient>""");

            Assert.Empty(g.DrawStringCalls);
            Assert.Equal(2, PathCalls(g).Count); // one filled outline per glyph
        }

        [Fact]
        public void TextPath_MissingPathReference_RendersAsOrdinaryRun()
        {
            // No <path> with that id: PathData stays null, so the run renders as ordinary straight text
            // (one DrawString for the whole run), not per-glyph.
            var g = Render("""<text x="10" y="50" font-size="40"><textPath href="#missing">Hi</textPath></text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("Hi", draw.Text);
        }

        [Fact]
        public void TextPath_StartOffsetPercent_ShiftsRunStartAlongThePath()
        {
            // startOffset=70% (=126 on a 180 path) pushes the run's start well down the path, so a wide
            // run loses its tail off the end. Compare against no offset (all glyphs fit).
            var noOffset = Render(
                """<text font-size="40"><textPath href="#p">Path</textPath></text>""",
                """<path id="p" d="M10,50 L190,50"/>""");
            var offset = Render(
                """<text font-size="40"><textPath href="#p" startOffset="70%">Path</textPath></text>""",
                """<path id="p" d="M10,50 L190,50"/>""");

            Assert.Equal(4, noOffset.DrawStringCalls.Count);
            Assert.True(offset.DrawStringCalls.Count < 4, "a large percentage startOffset should drop the run's tail off the path end");
        }

        [Fact]
        public void TextPath_StartOffsetLength_IsHonored()
        {
            // An absolute-length startOffset near the path end drops all but the first glyph or two.
            var g = Render(
                """<text font-size="40"><textPath href="#p" startOffset="160">Path</textPath></text>""",
                """<path id="p" d="M10,50 L190,50"/>""");

            Assert.True(g.DrawStringCalls.Count is >= 1 and < 4);
        }

        [Fact]
        public void TextPath_TextAnchorMiddle_CentersTheRunOnItsStart()
        {
            // text-anchor=middle shifts the run start back by half its width; with a mid-path natural
            // start the run still fits, but fewer glyphs than an equivalent end-anchored run past the end.
            var middle = Render(
                """<text font-size="40" text-anchor="middle"><textPath href="#p" startOffset="50%">Path</textPath></text>""",
                """<path id="p" d="M10,50 L190,50"/>""");
            var end = Render(
                """<text font-size="40" text-anchor="end"><textPath href="#p" startOffset="50%">Path</textPath></text>""",
                """<path id="p" d="M10,50 L190,50"/>""");

            Assert.True(middle.DrawStringCalls.Count >= end.DrawStringCalls.Count);
        }

        [Fact]
        public void TextPath_PatternFill_OutlinesEachGlyph_NoTextShow()
        {
            var g = Render(
                """<text font-size="40"><textPath href="#p" fill="url(#pat)">Hi</textPath></text>""",
                """<path id="p" d="M10,50 L190,50"/><pattern id="pat" width="8" height="8" patternUnits="userSpaceOnUse"><rect width="4" height="8" fill="orange"/></pattern>""");

            // A pattern fill outlines each glyph (no single-color text show). The tile itself needs a
            // page context to draw, which this recording graphics lacks, but the outline path is taken.
            Assert.Empty(g.DrawStringCalls);
        }

        [Fact]
        public void TextPath_Stroke_OutlinesAndStrokesEachGlyph()
        {
            var g = Render(
                """<text font-size="40"><textPath href="#p" fill="rgb(0,128,0)" stroke="rgb(0,0,255)" stroke-width="2">Hi</textPath></text>""",
                """<path id="p" d="M10,50 L190,50"/>""");

            Assert.Empty(g.DrawStringCalls);
            // Two glyphs, each filled then stroked -> four DrawPath calls.
            Assert.Equal(4, PathCalls(g).Count);
        }

        [Fact]
        public void TextPath_EmptyText_RendersNothing()
        {
            var g = Render(
                """<text font-size="40"><textPath href="#p"></textPath></text>""",
                """<path id="p" d="M10,50 L190,50"/>""");

            Assert.Empty(g.DrawStringCalls);
            Assert.Empty(PathCalls(g));
        }

        [Fact]
        public void TextPath_DegenerateZeroLengthPath_RendersNothing()
        {
            // A path with only a MoveTo has zero length: nothing to place glyphs along.
            var g = Render(
                """<text font-size="40"><textPath href="#p">Hi</textPath></text>""",
                """<path id="p" d="M10,50"/>""");

            Assert.Empty(g.DrawStringCalls);
        }

        [Fact]
        public void TextPath_InsideOpacityGroup_IsBoundedForItsGroupTile()
        {
            // A <g opacity> containing a textPath must measure the text's bounds (via the flattened
            // path bbox) to size its isolated-transparency-group tile - exercising the textPath branch of
            // the bounds accumulation. It renders without throwing and still places its glyphs.
            var g = Render(
                """<g opacity="0.5"><text font-size="40"><textPath href="#p">Hi</textPath></text></g>""",
                """<path id="p" d="M10,50 L190,50"/>""");

            Assert.Equal(2, g.DrawStringCalls.Count);
        }
    }
}
