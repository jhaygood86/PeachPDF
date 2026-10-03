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
    /// <summary><c>text-shadow</c> on SVG glyphs painted under their own transform: along a <c>&lt;textPath&gt;</c>, and blurred under an explicit <c>rotate</c>.</summary>
    public class SvgTextShadowTransformTests
    {
        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        /// <summary>A canvas that can begin an effect layer: it records each layer's options and draws the layer's content straight onto itself, so the log shows it.</summary>
        private sealed class LayerCanvas : TestRecordingGraphics
        {
            public List<LayerOptions> Layers { get; } = [];

            public override CanvasLayer? BeginLayer(LayerOptions options)
            {
                Layers.Add(options);
                return new CanvasLayer(this, () => { });
            }
        }

        private static T Render<T>(T g, string body, string viewBox = "0 0 200 100") where T : TestRecordingGraphics
        {
            var markup = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="{viewBox}">{body}</svg>""";
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var size = viewBox.Split(' ');
            SvgRenderer.RenderInto(g, document, new Rect(0, 0, double.Parse(size[2]), double.Parse(size[3])));
            return g;
        }

        private static TestRecordingGraphics.PushTransformCall[] GlyphPushes(TestRecordingGraphics g) =>
            g.Log.OfType<TestRecordingGraphics.PushTransformCall>().Skip(1).ToArray();

        [Fact]
        public void TextPath_Shadow_PaintsUnderEachGlyphOffsetInUserSpace()
        {
            // A 45 degree path, so a shadow offset that wrongly rotated with the glyph would show.
            var g = Render(new TestRecordingGraphics(),
                """<defs><path id="p" d="M10 10 L110 110"/></defs><text font-size="20" fill="rgb(0,0,255)" style="text-shadow: 5px 3px rgb(255,0,0)"><textPath href="#p">Hi</textPath></text>""",
                "0 0 200 200");

            Assert.Equal(4, g.DrawStringCalls.Count);
            // Shadow (red) then glyph (blue), per glyph.
            Assert.Equal([255, 0, 255, 0], g.DrawStringCalls.Select(c => (int)c.PaintColor.R).ToArray());
            var pushes = GlyphPushes(g);
            Assert.Equal(4, pushes.Length);
            for (var i = 0; i < 4; i += 2)
            {
                Assert.Equal(pushes[i + 1].Matrix.M31 + 5, pushes[i].Matrix.M31, 3);
                Assert.Equal(pushes[i + 1].Matrix.M32 + 3, pushes[i].Matrix.M32, 3);
                // The shadow keeps the glyph's rotation.
                Assert.Equal(pushes[i + 1].Matrix.M11, pushes[i].Matrix.M11, 3);
                Assert.Equal(pushes[i + 1].Matrix.M12, pushes[i].Matrix.M12, 3);
                Assert.NotEqual(0, pushes[i].Matrix.M12, 3);
            }

            // Same position in the glyph's own frame for both.
            Assert.Equal(g.DrawStringCalls[1].PaintPoint, g.DrawStringCalls[0].PaintPoint);
        }

        [Fact]
        public void TextPath_NoShadow_PaintsOnlyTheGlyphs()
        {
            var g = Render(new TestRecordingGraphics(),
                """<defs><path id="p" d="M10 60 L110 60"/></defs><text font-size="20"><textPath href="#p">Hi</textPath></text>""");

            Assert.Equal(2, g.DrawStringCalls.Count);
            Assert.Equal(2, GlyphPushes(g).Length);
        }

        [Fact]
        public void TextPath_BlurredShadow_DrawsThroughTheGlyphFrameIntoABlurLayer()
        {
            var g = Render(new LayerCanvas(),
                """<defs><path id="p" d="M10 10 L110 110"/></defs><text font-size="20" fill="rgb(0,0,255)" style="text-shadow: 5px 3px 4px rgb(255,0,0)"><textPath href="#p">Hi</textPath></text>""",
                "0 0 200 200");

            Assert.Equal(2, g.Layers.Count);
            Assert.All(g.Layers, l =>
            {
                Assert.Equal(2.0, Assert.IsType<BlurEffect>(Assert.Single(l.Effects!)).SigmaX, 3);
                Assert.True(l.Bounds is { Width: > 0, Height: > 0 });
            });

            // The shadow of each glyph is drawn in the layer through the glyph's own frame (rotated), offset in user space.
            var pushes = GlyphPushes(g);
            Assert.Equal(4, pushes.Length);
            Assert.Equal(pushes[1].Matrix.M31 + 5, pushes[0].Matrix.M31, 3);
            Assert.Equal(pushes[1].Matrix.M32 + 3, pushes[0].Matrix.M32, 3);
        }

        [Fact]
        public void BlurredShadow_OnRotatedGlyph_IsBlurredThroughTheRotation()
        {
            var g = Render(new LayerCanvas(), """<text x="50" y="50" font-size="20" rotate="90" fill="rgb(0,0,255)" style="text-shadow: 6px 0 4px rgb(255,0,0)">H</text>""");

            var layer = Assert.Single(g.Layers);
            Assert.Equal(2.0, Assert.IsType<BlurEffect>(Assert.Single(layer.Effects!)).SigmaX, 3);

            // Shadow then glyph, each under its own push; the shadow's matrix is the glyph's moved 6 units right in user space.
            var pushes = GlyphPushes(g);
            Assert.Equal(2, pushes.Length);
            Assert.Equal(pushes[1].Matrix.M31 + 6, pushes[0].Matrix.M31, 3);
            Assert.Equal(pushes[1].Matrix.M32, pushes[0].Matrix.M32, 3);
            Assert.Equal(2, g.DrawStringCalls.Count);

            // The layer covers the rotated glyph box: a glyph rotated 90 degrees about (50, 50) lies to the left of x=50, the shadow 6 further right.
            var bounds = layer.Bounds!.Value;
            var glyph = g.DrawStringCalls[0];
            var centre = Transform(pushes[0].Matrix, glyph.PaintPoint.X + glyph.Size.Width / 2, glyph.PaintPoint.Y + glyph.Size.Height / 2);
            Assert.True(bounds.X <= centre.X && centre.X <= bounds.Right, $"x {centre.X} outside {bounds}");
            Assert.True(bounds.Y <= centre.Y && centre.Y <= bounds.Bottom, $"y {centre.Y} outside {bounds}");
            // Wide enough for the blur's spread on every side of the rotated box (a 90 degree rotation swaps its width and height).
            Assert.True(bounds.Width >= glyph.Size.Height + 6 * 1.0);
        }

        [Fact]
        public void UnblurredShadow_OnRotatedGlyph_StillNeedsNoLayer()
        {
            var g = Render(new LayerCanvas(), """<text x="50" y="50" font-size="20" rotate="90" style="text-shadow: 6px 0 red">H</text>""");

            Assert.Empty(g.Layers);
        }

        private static (double X, double Y) Transform(System.Numerics.Matrix3x2 m, double x, double y) =>
            (x * m.M11 + y * m.M21 + m.M31, x * m.M12 + y * m.M22 + m.M32);
    }
}
