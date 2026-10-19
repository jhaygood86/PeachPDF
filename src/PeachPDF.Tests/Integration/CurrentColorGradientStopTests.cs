using PeachPDF.Rendering;
using PeachDrawing.Core;
using GradientStop = PeachDrawing.Core.GradientStop;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Parse;
using PeachDrawing.PDF;
using PeachDrawing.PDF.Engine.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>currentcolor</c> as a colour stop of a gradient: the CSS-OM grammar accepts it (it used to drop the whole declaration, so
    /// the box drew nothing), the parser records the stop, and the painter gives it the painting box's own color.
    /// </summary>
    public class CurrentColorGradientStopTests
    {
        private static readonly CssValueParser Parser = new(new HtmlRenderContext());

        // ─── Parsing ─────────────────────────────────────────────────────────────

        [Theory]
        [InlineData("linear-gradient(to right, currentcolor, red)", "0")]
        [InlineData("linear-gradient(to right, red, CurrentColor)", "1")]
        [InlineData("linear-gradient(red, currentColor 40%, blue)", "1")]
        [InlineData("linear-gradient(currentcolor 0 50%, blue)", "0,1")]          // two positions: two stops, both flagged
        [InlineData("repeating-linear-gradient(currentcolor, red 20px)", "0")]
        [InlineData("radial-gradient(circle, currentcolor, red)", "0")]
        [InlineData("radial-gradient(red, currentcolor 30%, blue)", "1")]
        [InlineData("conic-gradient(from 90deg, currentcolor, red)", "0")]
        [InlineData("conic-gradient(red, currentcolor 90deg, blue)", "1")]
        public void ParseImages_RecordsTheCurrentColorStops(string value, string expectedIndexes)
        {
            var images = Parser.ParseImages(value);

            Assert.NotNull(images);
            var indexes = images[0] switch
            {
                CssImage.LinearGradient g => g.Gradient.CurrentColorStops,
                CssImage.RadialGradient g => g.Gradient.CurrentColorStops,
                CssImage.ConicGradient g => g.Gradient.CurrentColorStops,
                _ => throw new InvalidOperationException()
            };

            Assert.Equal(expectedIndexes, string.Join(",", indexes ?? []));
        }

        [Fact]
        public void ParseImages_WithoutCurrentColor_RecordsNone()
        {
            var image = Assert.IsType<CssImage.LinearGradient>(Parser.ParseImages("linear-gradient(red, blue)")![0]);

            Assert.Null(image.Gradient.CurrentColorStops);
        }

        // ─── Painting ────────────────────────────────────────────────────────────

        private static async Task<CssBox> BuildAsync(string html, double? progress = null)
        {
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { AnimationProgress = progress };
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.ToPixels(size, 1.0);

            var measure = PdfGraphics.CreateMeasureContext(size);
            using var graphics = new PdfCanvas(renderContext, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return Find(container.Root!, "a") ?? throw new InvalidOperationException("No #a");
        }

        private static CssBox? Find(CssBox box, string id)
        {
            if (box.HtmlTag?.Attributes?.TryGetValue("id", out var boxId) == true && string.Equals(boxId, id, StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                if (Find(child, id) is { } found) return found;
            }

            return null;
        }

        /// <summary>The stops of the first background layer's brush, as the painter builds it for <paramref name="box"/>.</summary>
        private static IReadOnlyList<GradientStop> BackgroundBrushStops(CssBox box)
        {
            var renderContext = new HtmlRenderContext();
            var measure = PdfGraphics.CreateMeasureContext(new Size(595, 842));
            using var g = new PdfCanvas(renderContext, measure, 1.0);
            var rect = new Rect(0, 0, 200, 100);

            return box.BackgroundImages![0] switch
            {
                CssImage.LinearGradient lg => ((LinearGradientBrush)CssImagePainter.GetLinearGradientBrush(g, lg.Gradient, rect, box, null)).Stops,
                CssImage.RadialGradient rg => ((RadialGradientBrush)CssImagePainter.GetRadialGradientBrush(g, rg.Gradient, rect, box, null)).Stops,
                CssImage.ConicGradient cg => ((ConicGradientBrush)CssImagePainter.GetConicGradientBrush(g, cg.Gradient, rect, box)).Stops,
                _ => throw new InvalidOperationException()
            };
        }

        private static void AssertStop(GradientStop stop, int r, int g, int b)
        {
            Assert.InRange(stop.PaintColor.R, r - 1, r + 1);
            Assert.InRange(stop.PaintColor.G, g - 1, g + 1);
            Assert.InRange(stop.PaintColor.B, b - 1, b + 1);
        }

        [Theory]
        [InlineData("linear-gradient(to right, currentcolor, rgb(255, 0, 0))")]
        [InlineData("radial-gradient(circle, currentcolor, rgb(255, 0, 0))")]
        [InlineData("conic-gradient(currentcolor, rgb(255, 0, 0))")]
        public async Task TheStop_TakesTheBoxsColor(string gradient)
        {
            var box = await BuildAsync($@"<div id=""a"" style=""color: rgb(0, 0, 255); width: 200px; height: 100px; background-image: {gradient}"">x</div>");

            var stops = BackgroundBrushStops(box);

            AssertStop(stops[0], 0, 0, 255);
            AssertStop(stops[^1], 255, 0, 0);
        }

        [Theory]
        [InlineData("linear-gradient(to right, currentcolor, rgb(255, 0, 0))")]
        [InlineData("radial-gradient(circle, currentcolor, rgb(255, 0, 0))")]
        [InlineData("conic-gradient(currentcolor, rgb(255, 0, 0))")]
        public async Task TheLayerPath_PaintsTheStopInTheBoxsColor(string gradient)
        {
            // Through CssImagePainter.Paint, the entry every background layer goes through, not the brush builders alone.
            var box = await BuildAsync($@"<div id=""a"" style=""color: rgb(0, 0, 255); width: 200px; height: 100px; background-image: {gradient}"">x</div>");
            var renderContext = new HtmlRenderContext();
            using var g = new PdfCanvas(renderContext, PdfGraphics.CreateMeasureContext(new Size(595, 842)), 1.0);
            var rect = new Rect(0, 0, 200, 100);
            Brush? painted = null;

            CssImagePainter.Paint(g, box.BackgroundImages![0], 0, rect, rect, null, "0% 0%", "auto", "repeat", "scroll", rect, box, brush => painted = brush);

            var stops = painted switch
            {
                LinearGradientBrush b => b.Stops,
                RadialGradientBrush b => b.Stops,
                ConicGradientBrush b => b.Stops,
                _ => throw new InvalidOperationException($"No gradient brush was drawn: {painted}")
            };
            AssertStop(stops[0], 0, 0, 255);
            AssertStop(stops[^1], 255, 0, 0);
        }

        [Fact]
        public async Task TheStop_TakesAnInheritedColor_AndTheBackgroundShorthandWorks()
        {
            var box = await BuildAsync(@"<div style=""color: rgb(0, 128, 0)""><div id=""a"" style=""width: 200px; height: 100px; background: linear-gradient(to right, rgb(255, 0, 0), currentcolor)"">x</div></div>");

            var stops = BackgroundBrushStops(box);

            AssertStop(stops[0], 255, 0, 0);
            AssertStop(stops[^1], 0, 128, 0);
        }

        [Fact]
        public async Task TheStop_FollowsTheColorDeclaredAfterIt()
        {
            // The color is final only when the box paints, which is why the stop is resolved there and not when it is parsed.
            var box = await BuildAsync(@"<div id=""a"" style=""background-image: linear-gradient(currentcolor, rgb(255, 0, 0)); color: rgb(0, 0, 255); width: 200px; height: 100px"">x</div>");

            AssertStop(BackgroundBrushStops(box)[0], 0, 0, 255);
        }

        [Fact]
        public async Task TwoPositionStop_ResolvesBothStops()
        {
            var box = await BuildAsync(@"<div id=""a"" style=""color: rgb(0, 0, 255); width: 200px; height: 100px; background-image: linear-gradient(currentcolor 0 50%, rgb(255, 0, 0))"">x</div>");

            var stops = BackgroundBrushStops(box);

            Assert.Equal(3, stops.Count);
            AssertStop(stops[0], 0, 0, 255);
            AssertStop(stops[1], 0, 0, 255);
            AssertStop(stops[2], 255, 0, 0);
        }

        [Fact]
        public async Task ConicGradient_WithoutABox_ResolvesToBlack()
        {
            var box = await BuildAsync(@"<div id=""a"" style=""color: rgb(0, 0, 255); width: 200px; height: 100px; background-image: conic-gradient(currentcolor, rgb(255, 0, 0))"">x</div>");
            var renderContext = new HtmlRenderContext();
            using var g = new PdfCanvas(renderContext, PdfGraphics.CreateMeasureContext(new Size(595, 842)), 1.0);
            var cg = Assert.IsType<CssImage.ConicGradient>(box.BackgroundImages![0]);

            var brush = (ConicGradientBrush)CssImagePainter.GetConicGradientBrush(g, cg.Gradient, new Rect(0, 0, 200, 100));

            AssertStop(brush.Stops[0], 0, 0, 0);
        }

        // ─── Page and page-margin boxes ──────────────────────────────────────────

        [Fact]
        public async Task PageAndMarginBoxGradients_TakeTheirOwnColor_NotTheRootBoxs()
        {
            // Each of the three paints with the root box as its geometry stand-in, but currentcolor is that box's own `color`: green
            // for the @page background, red for the top-centre content image, blue for the bottom-centre background (the root
            // element's colour here is the default black, which is what they all used to get).
            var pairs = ShadingColorPairs(await RenderPdfAsync("""
                <!DOCTYPE html><html><head><style>
                @page { size: 400px 300px; margin: 60px; background: linear-gradient(to right, currentcolor, white); color: #008800;
                  @top-center { color: #ff0000; content: linear-gradient(to right, currentcolor, blue); width: 200px; height: 30px }
                  @bottom-center { color: #0000ff; background: linear-gradient(to right, currentcolor, yellow); content: "x" } }
                </style></head><body>hi</body></html>
                """));

            Assert.Contains("0 0.533 0 -> 1 1 1", pairs);
            Assert.Contains("1 0 0 -> 0 0 1", pairs);
            Assert.Contains("0 0 1 -> 1 1 0", pairs);
        }

        [Fact]
        public async Task TwoMarginBoxes_WithTheSameGradientText_EachTakeTheirOwnColor()
        {
            // The parsed gradient is cached by its text and shared; each box still resolves its own stop.
            var pairs = ShadingColorPairs(await RenderPdfAsync("""
                <!DOCTYPE html><html><head><style>
                @page { size: 400px 300px; margin: 60px;
                  @top-left { color: #ff0000; background: linear-gradient(to right, currentcolor, white); content: "a" }
                  @top-right { color: #0000ff; background: linear-gradient(to right, currentcolor, white); content: "b" } }
                </style></head><body>hi</body></html>
                """));

            Assert.Contains("1 0 0 -> 1 1 1", pairs);
            Assert.Contains("0 0 1 -> 1 1 1", pairs);
        }

        [Fact]
        public async Task PageBackground_WithoutAColorOfItsOwn_TakesBlack_NotTheRootElementsColor()
        {
            var pdf = await RenderPdfAsync("""
                <!DOCTYPE html><html style="color: #cc0000"><head><style>
                @page { size: 400px 300px; background: linear-gradient(to right, currentcolor, white) }
                </style></head><body>hi</body></html>
                """);

            Assert.Contains("0 0 0 -> 1 1 1", ShadingColorPairs(pdf));
        }

        private static async Task<string> RenderPdfAsync(string html)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(html, config);
            var stream = new System.IO.MemoryStream();
            doc.Save(stream);
            return System.Text.Encoding.Latin1.GetString(stream.ToArray());
        }

        /// <summary>Every axial/radial shading's colours as <c>"c0 -> c1"</c>, read from the function dictionaries.</summary>
        private static List<string> ShadingColorPairs(string pdf) =>
            System.Text.RegularExpressions.Regex.Matches(pdf, @"/C0\s*\[([^\]]*)\]\s*/C1\s*\[([^\]]*)\]")
                .Select(m => $"{m.Groups[1].Value.Trim()} -> {m.Groups[2].Value.Trim()}").ToList();

        /// <summary>The bytes of every mesh shading stream (a conic gradient is a type 4 shading), which carry its colours.</summary>
        private static List<string> MeshShadingStreams(string pdf) =>
            System.Text.RegularExpressions.Regex.Matches(pdf, @"/ShadingType 4.*?stream\r?\n(.*?)endstream", System.Text.RegularExpressions.RegexOptions.Singleline)
                .Select(m => m.Groups[1].Value).ToList();

        [Theory]
        [InlineData("linear-gradient(to right, currentcolor, #ff0000)", "linear-gradient(to right, #0000ff, #ff0000)")]
        [InlineData("conic-gradient(currentcolor, #ff0000)", "conic-gradient(#0000ff, #ff0000)")]
        public async Task BorderImageSource_PaintsTheStopInTheBoxsColor(string withKeyword, string withLiteral)
        {
            // The same document with the colour written out must paint the same shading: compared by the shading's own bytes, so a
            // border-image gradient that took black (or nothing) for the keyword differs.
            static string Html(string gradient) => $@"<!DOCTYPE html><html><body style=""margin: 0; color: #0000ff"">
                <div style=""width: 120px; height: 60px; border: 20px solid; border-image-source: {gradient}; border-image-slice: 1""></div></body></html>";

            var keyword = await RenderPdfAsync(Html(withKeyword));
            var literal = await RenderPdfAsync(Html(withLiteral));

            if (withKeyword.StartsWith("conic", StringComparison.Ordinal))
            {
                var keywordStreams = MeshShadingStreams(keyword);
                Assert.NotEmpty(keywordStreams);
                Assert.Equal(MeshShadingStreams(literal), keywordStreams);
            }
            else
            {
                Assert.Contains("0 0 1 -> 1 0 0", ShadingColorPairs(keyword));
                Assert.Equal(ShadingColorPairs(literal), ShadingColorPairs(keyword));
            }
        }

        [Theory]
        [InlineData("repeating-linear-gradient(to right, currentcolor 0, rgb(255, 0, 0) 50px)")]
        [InlineData("repeating-radial-gradient(circle, currentcolor 0, rgb(255, 0, 0) 20px)")]
        [InlineData("repeating-conic-gradient(currentcolor 0 10%, rgb(255, 0, 0) 0 20%)")]
        [InlineData("-webkit-linear-gradient(left, currentcolor, rgb(255, 0, 0))")]
        [InlineData("-moz-radial-gradient(center, circle, currentcolor, rgb(255, 0, 0))")]
        public async Task RepeatingAndPrefixedForms_TakeTheBoxsColor(string gradient)
        {
            var box = await BuildAsync($@"<div id=""a"" style=""color: rgb(0, 0, 255); width: 200px; height: 100px; background-image: {gradient}"">x</div>");

            var stops = BackgroundBrushStops(box);

            // A repeating gradient's stops are expanded to fill the box: the blue (the keyword) and the red both appear.
            Assert.Contains(stops, stop => stop.PaintColor.B >= 254 && stop.PaintColor.R <= 1);
            Assert.Contains(stops, stop => stop.PaintColor.R >= 254 && stop.PaintColor.B <= 1);
        }

        // ─── Animation: the mixer now sees the stop ──────────────────────────────

        [Fact]
        public async Task AnimatedGradient_FromACurrentColorStop_MixesWithTheBoxsColor()
        {
            // The first stop goes from currentcolor (blue) to red: purple half way.
            var box = await BuildAsync(@"
                <style>@keyframes k { from { background-image: linear-gradient(currentcolor, rgb(255, 255, 255)) } to { background-image: linear-gradient(rgb(255, 0, 0), rgb(255, 255, 255)) } }
                #a { color: rgb(0, 0, 255); width: 200px; height: 100px; animation: k 1s linear }</style><div id=""a"">x</div>", 0.5);

            var stops = BackgroundBrushStops(box);

            AssertStop(stops[0], 128, 0, 128);
            AssertStop(stops[^1], 255, 255, 255);
        }
    }
}
