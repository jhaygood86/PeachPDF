using PeachPDF;
using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A page-margin box's own <c>border</c> (closes #943): painted as four independent edge strokes
    /// (<see cref="PeachPDF.Html.Core.Dom.MarginBoxRenderer.PaintBorder"/>, via
    /// <see cref="PeachPDF.Html.Core.Handlers.BordersDrawHandler.DrawCollapsedSegment"/>'s solid-style
    /// path, which paints a 4-point polygon fill - <c>X Y m / X Y l / X Y l / X Y l / h f</c> in the raw
    /// content stream). Verified the same content-stream-regex way as the sibling background/canvas
    /// tests: a solid-color polygon fill's exact vertex coordinates are unambiguous proof, confirmed
    /// against this library's actual coordinate convention (PDF Y is flipped from document Y - a
    /// "border-top" edge ends up at the HIGH end of the box's own PDF-space Y range).
    /// <para>
    /// The per-side bevel tests at the end take the other route, painting
    /// <see cref="MarginBoxRenderer.PaintBorder"/> through a recording <c>RGraphics</c> instead: they
    /// assert which <em>face</em> each edge takes, and an exact <c>RColor</c> says that where an
    /// <c>rg</c> operator rounded to three decimals only says it approximately.
    /// </para>
    /// </summary>
    public class MarginBoxRendererBorderTests
    {
        private static async Task<string> GetPdfText(string html, int margin = 40)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.Undefined,
                ManualPageWidth = 600,
                ManualPageHeight = 800,
                CompressContentStreams = false,
            };
            config.SetMargins(margin);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        private static Regex PolygonFillPattern(string colorOp, double x1, double y1, double x2, double y2) =>
            new(Regex.Escape(colorOp) + @"\s*\n" +
                $@"{x1}(\.\d+)? {y1}(\.\d+)? m\s*\n" +
                $@"{x2}(\.\d+)? {y1}(\.\d+)? l\s*\n" +
                $@"{x2}(\.\d+)? {y2}(\.\d+)? l\s*\n" +
                $@"{x1}(\.\d+)? {y2}(\.\d+)? l\s*\nh f");

        [Fact]
        public async Task AllFourEdges_PaintTheirOwnIndependentlyResolvedWidthAndColor()
        {
            // bottom-left/-right pin bottom-center's own slot to start at x:60. width:200pt is the
            // CONTENT-box dimension (css-page-3 §5.3.2) - border is additive on top of it, so with a 6pt
            // left / 2pt right border the box's own OUTER (= border-box, no margin declared) slot is
            // 200+6+2=208pt wide, x:60..268, not 60..260. mB=40 -> doc-space Y 760..800, which this
            // library's PDF Y-flip maps to PDF-space Y 0..40 (Y = pageHeight - docY).
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>@page { @bottom-left { content: \"L\"; width: 20pt; } " +
                "@bottom-center { content: \"x\"; width: 200pt; " +
                "border-top: 4pt solid rgb(255,0,0); border-bottom: 8pt solid rgb(0,0,255); " +
                "border-left: 6pt solid rgb(0,255,0); border-right: 2pt solid rgb(0,0,0); } " +
                "@bottom-right { content: \"R\"; width: 20pt; } }</style></head>" +
                "<body><p>short</p></body></html>");

            // top: full border-box width (60..268), PDF Y from 40 (border-box top) down to 40-4=36.
            Assert.Matches(PolygonFillPattern("1 0 0 rg", 60, 40, 268, 36), pdfText);
            // bottom: full border-box width, PDF Y from 8 (border-box bottom + bottom width) down to 0.
            Assert.Matches(PolygonFillPattern("0 0 1 rg", 60, 8, 268, 0), pdfText);
            // left: PDF X from 60 (border-box left) to 60+6=66, spanning the full box height.
            Assert.Matches(PolygonFillPattern("0 1 0 rg", 60, 40, 66, 0), pdfText);
            // right: PDF X from 268-2=266 to 268 (border-box right).
            Assert.Matches(PolygonFillPattern("0 0 0 rg", 266, 40, 268, 0), pdfText);
        }

        [Fact]
        public async Task UnsetBorderStyle_PaintsNoEdge_EvenWithColorAndWidthDeclared()
        {
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>@page { @bottom-center { content: \"x\"; width: 200pt; " +
                "border-top-width: 10pt; border-top-color: red; } }</style></head>" +
                "<body><p>short</p></body></html>");

            Assert.DoesNotContain("1 0 0 rg", pdfText);
        }

        [Fact]
        public async Task ContentElementMarginBox_PaintsTheRuleOwnBackgroundAndBorder()
        {
            // The content: element() path (HtmlContainerInt.LayoutMarginBoxes / PdfGenerator.PaintElementMarginBoxes)
            // is a separate pipeline from the text-content path above - this exercises that the margin-box
            // RULE's own background/border (not the running element's) is threaded through it too.
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>" +
                "@page { @top-center { content: element(heading); background-color: rgb(255,0,255); border-bottom: 3pt solid black; } }" +
                "h1 { position: running(heading); }" +
                "</style></head><body><h1>Chapter</h1><p>short</p></body></html>");

            // Background: a solid magenta fill somewhere on the page (the margin box's own border-box rect).
            Assert.Contains("1 0 1 rg", pdfText);
            // Border: a black polygon fill (border-bottom).
            Assert.Contains("0 0 0 rg", pdfText);
        }

        [Fact]
        public async Task ContentElementMarginBox_PaintsItsOwnBackground_EvenWhenTheRunningElementNeverResolves()
        {
            // A margin box's background/border paints "unconditionally", independent of content - the
            // text-content path already proves that for an ordinary box with content:none. This is the
            // same guarantee for a content:element(name) box where no position:running(name) exists
            // anywhere in the document at all (a typo, or simply not authored yet): HtmlContainerInt.
            // LayoutMarginBoxes creates no MarginBoxFragment for it in that case, so
            // PdfGenerator.PaintElementMarginBoxes must still find and paint the rule's own
            // background/border by walking the applicable margin rules themselves, not just the
            // fragments that happened to resolve.
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>" +
                "@page { @top-center { content: element(nonexistent); background-color: rgb(255,0,255); } " +
                // A second, ordinary text-content rule on the same page - PaintElementMarginBoxes' second
                // loop must skip this one (it isn't an element() rule) rather than double-painting it.
                "@bottom-center { content: \"footer\"; } }" +
                "</style></head><body><p>short</p></body></html>");

            Assert.Contains("1 0 1 rg", pdfText);
        }

        [Fact]
        public async Task ABevelledEdgeWithNoDeclaredColor_ShadesTheFixedBase_NotTheBoxsText()
        {
            // This path resolves `currentcolor` itself rather than through an ordinary box's
            // DerivedStyle.ResolveBorderSideColor, so it needs its own copy of the bevel-base rule
            // (issue #1226): a bevelled side with no declared colour shades rgb(238,238,238), not the
            // box's text colour. Without it, `@page { border: inset }` would shade the declared red here
            // while the identical declaration on a <div> paints the two greys - a divergence entirely
            // invisible to the border tests above, every one of which declares its own colour.
            //
            // The `border-top` SHORTHAND with its colour slot omitted, deliberately: that slot exports
            // as the literal "initial", which this path used to run through GetActualColor and get
            // black out of - so it shaded 0.329 (Light(black)) where a <div> with the identical
            // declaration paints the two greys. Asserting the longhand form instead would pass
            // without the "initial" arm and prove nothing.
            //
            // 0.604 = 154/255, the darkened face of rgb(238,238,238) - the same byte an unstyled <hr>
            // paints - on the TOP edge, and 0.933 = 238/255, its lit face, on the bottom one (that face
            // keeps the declared colour rather than lightening, rgb(238,238,238) being past the
            // near-white threshold). Both are asserted because both used to be 0.604: every edge was
            // shaded as if it were a top/left one, so no lit face was produced at all (issue #1237).
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>@page { @bottom-center { content: \"x\"; width: 200pt; " +
                "color: rgb(255,0,0); border-top: 4pt inset; border-bottom: 4pt inset; } }</style></head>" +
                "<body><p>short</p></body></html>");

            Assert.Contains("0.604 0.604 0.604 rg", pdfText);
            Assert.Contains("0.933 0.933 0.933 rg", pdfText);
            // ...and emphatically neither the shaded red nor the shaded black it produced before.
            Assert.DoesNotContain("0.671 0 0 rg", pdfText);
            Assert.DoesNotContain("0.329 0.329 0.329 rg", pdfText);
        }

        [Theory]
        // Every flat style, so the bevel arm cannot be widened to "unset colour" without failing here.
        [InlineData("solid")]
        [InlineData("double")]
        public async Task AFlatEdgeWithNoDeclaredColor_StillResolvesThroughColor(string style)
        {
            // The other half of the same rule, and the one the bevel arm must not swallow: nothing is
            // derived from a flat edge, so `currentcolor` means the box's own colour as it always has.
            // `color` is deliberately non-default here - against the default black, returning the base
            // unconditionally and returning it only for a bevel are indistinguishable.
            var pdfText = await GetPdfText(
                "<!DOCTYPE html><html><head><style>@page { @bottom-center { content: \"x\"; width: 200pt; " +
                $"color: rgb(255,0,0); border-top: 4pt {style}; }} }}</style></head>" +
                "<body><p>short</p></body></html>");

            Assert.Contains("1 0 0 rg", pdfText);
            // 0.933 = 238/255: the base must not reach a flat edge at all.
            Assert.DoesNotContain("0.933 0.933 0.933 rg", pdfText);
            Assert.DoesNotContain("0.604 0.604 0.604 rg", pdfText);
        }

        // ── Per-side bevel faces (issue #1237) ───────────────────────────────────────────────────
        //
        // Painted through TestRecordingGraphics rather than the content stream: these assert which
        // FACE each edge takes, and an exact RColor says that where a rounded 3-decimal "rg" operator
        // only says it approximately.

        /// <summary>A 100×50 border box with a 10pt border on every side, painted directly.</summary>
        private static List<TestRecordingGraphics.FilledShape> PaintMarginBoxBorder(string declarations)
        {
            var style = new StylesheetParser()
                .Parse($"@page {{ @top-center {{ content: \"x\"; {declarations} }} }}")
                .Rules.OfType<PageRule>().Single().Margins.Single().Style;

            var g = new TestRecordingGraphics();
            MarginBoxRenderer.PaintBorder(
                g, new RRect(0, 0, 100, 50), style, emPt: 16, remPt: 16, pixelsPerPoint: 1.0,
                adapter: new PdfSharpAdapter());

            // FilledShapes rather than DrawPolygonCall: a fill is a fill whichever primitive made it,
            // so a future ring-path fast path here would not silently read as "nothing painted".
            return [.. g.FilledShapes];
        }

        /// <summary>
        /// The colour of the one band covering exactly the given rect, with a failure that names every
        /// band it did find - an exact-rect dictionary lookup would throw KeyNotFound and say nothing
        /// about what moved.
        /// </summary>
        private static RColor FaceAt(
            IReadOnlyList<TestRecordingGraphics.FilledShape> bands,
            double left, double top, double right, double bottom)
        {
            var want = RRect.FromLTRB(left, top, right, bottom);
            var matches = bands.Where(b => Matches(b.Bounds, want)).ToList();

            Assert.True(matches.Count == 1,
                $"expected exactly one band at {want}, found {matches.Count} among: " +
                string.Join(", ", bands.Select(b => b.Bounds)));

            return matches[0].Color;
        }

        /// <summary>Whether two rects name the same band, to within paint-coordinate noise.</summary>
        private static bool Matches(RRect actual, RRect expected) =>
            Math.Abs(actual.Left - expected.Left) < 0.001 && Math.Abs(actual.Top - expected.Top) < 0.001 &&
            Math.Abs(actual.Right - expected.Right) < 0.001 && Math.Abs(actual.Bottom - expected.Bottom) < 0.001;

        [Fact]
        public void ABevelledMarginBoxBorder_TakesEachSidesOwnFace()
        {
            // A margin box is a real box with four real sides, unlike a collapsed table's grid lines,
            // so inset darkens top and left and lights bottom and right the way any other box's border
            // does. Before #1237 all four darkened - the frame came out flat, with no bevel at all.
            var bands = PaintMarginBoxBorder("border: 10pt inset rgb(128,128,128)");

            var color = RColor.FromArgb(128, 128, 128);
            var dark = BorderBevelColors.Shade(color, darken: true);
            var light = BorderBevelColors.Shade(color, darken: false);
            Assert.NotEqual(dark, light);

            Assert.Equal(4, bands.Count);

            Assert.Equal(dark, FaceAt(bands, 0, 0, 100, 10));    // top
            Assert.Equal(light, FaceAt(bands, 0, 40, 100, 50));  // bottom
            Assert.Equal(dark, FaceAt(bands, 0, 0, 10, 50));     // left
            Assert.Equal(light, FaceAt(bands, 90, 0, 100, 50));  // right
        }

        [Fact]
        public void AnOutsetMarginBoxBorder_TakesTheMirrorFaceOfInset()
        {
            // The other half of the same rule: outset must not be "inset for every side" either.
            var bands = PaintMarginBoxBorder("border: 10pt outset rgb(128,128,128)");

            var color = RColor.FromArgb(128, 128, 128);
            var dark = BorderBevelColors.Shade(color, darken: true);
            var light = BorderBevelColors.Shade(color, darken: false);

            Assert.Equal(4, bands.Count);

            Assert.Equal(light, FaceAt(bands, 0, 0, 100, 10));   // top
            Assert.Equal(dark, FaceAt(bands, 0, 40, 100, 50));   // bottom
            Assert.Equal(light, FaceAt(bands, 0, 0, 10, 50));    // left
            Assert.Equal(dark, FaceAt(bands, 90, 0, 100, 50));   // right
        }

        [Fact]
        public void AGrooveMarginBoxBorder_PutsItsInsetFaceOnTheOuterHalfOfEverySide()
        {
            // groove's outer half is its inset face (CSS 2.1 §8.5.3) - and "outer" is the half at the
            // SMALLER coordinate on a top/left edge but the larger one on a bottom/right edge. Both
            // flips together are what make a bottom edge come out the way it does, and they cancel: a
            // bottom edge's bands are the same two colours in the same order as a top edge's, which is
            // why groove/ridge survived #1237's side-blind shading unharmed where inset/outset did not.
            // Pinned here so that a change teaching this path about sides cannot flip one without the
            // other and still look right.
            var bands = PaintMarginBoxBorder("border: 10pt groove rgb(128,128,128)");

            var color = RColor.FromArgb(128, 128, 128);
            var dark = BorderBevelColors.Shade(color, darken: true);
            var light = BorderBevelColors.Shade(color, darken: false);

            Assert.Equal(8, bands.Count);

            // Top: outer band first, and it is the inset face of a top edge - darkened.
            Assert.Equal(dark, FaceAt(bands, 0, 0, 100, 5));
            Assert.Equal(light, FaceAt(bands, 0, 5, 100, 10));

            // Bottom: the outer band is the LOWER one, and the inset face of a bottom edge is lit.
            Assert.Equal(dark, FaceAt(bands, 0, 40, 100, 45));
            Assert.Equal(light, FaceAt(bands, 0, 45, 100, 50));

            Assert.Equal(dark, FaceAt(bands, 0, 0, 5, 50));
            Assert.Equal(light, FaceAt(bands, 5, 0, 10, 50));

            Assert.Equal(dark, FaceAt(bands, 90, 0, 95, 50));
            Assert.Equal(light, FaceAt(bands, 95, 0, 100, 50));
        }
    }
}
