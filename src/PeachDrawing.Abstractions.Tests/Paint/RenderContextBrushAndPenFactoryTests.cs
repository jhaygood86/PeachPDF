using PeachDrawing.Text;
using PeachDrawing.Text.Unicode;

namespace PeachDrawing.Abstractions.Tests.Paint
{
    public class RenderContextBrushAndPenFactoryTests
    {
        /// <summary>
        /// The minimal concrete <see cref="RenderContext"/> the <c>Get*Brush</c>/<c>GetPen</c> factories
        /// need to exercise: none of the factory methods under test call any of these abstract members, so
        /// every stub just throws - a test that accidentally reaches one fails loudly instead of silently
        /// returning a stub value.
        /// </summary>
        private sealed class TestRenderContext : RenderContext
        {
            public override string GetCssMediaType(IEnumerable<string> mediaTypesAvailable) => throw new NotSupportedException();
            protected override PaintColor GetColorInt(string colorName) => throw new NotSupportedException();
            protected override Image ImageFromStreamInt(Stream memoryStream) => throw new NotSupportedException();
            protected override Font CreateFontInt(string family, double size, PaintFontStyle style, double weight = 400, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null) => throw new NotSupportedException();
            protected override Font CreateFontInt(FontFamily family, double size, PaintFontStyle style, double weight = 400, double stretch = 100, double? obliqueSkewSinus = null, string? variations = null) => throw new NotSupportedException();
            protected override Font? CreateFontForCodepointInt(string family, double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, string? variations) => throw new NotSupportedException();
            protected override Font? CreateSystemFallbackFontForCodepointInt(double size, PaintFontStyle style, double weight, double stretch, double? obliqueSkewSinus, System.Text.Rune codepoint, PeachDrawing.Text.Unicode.EmojiPresentation presentation, string? variations) => throw new NotSupportedException();
            protected override bool FamilyHasExplicitUnicodeRangesInt(string family) => throw new NotSupportedException();
            protected override Task<bool> AddFontFromStream(string fontFamilyName, Stream stream, string? format, FontFaceDescriptors descriptors = default, IReadOnlyList<RuneInterval>? unicodeRanges = null) => throw new NotSupportedException();
            protected override Task<bool> AddLocalFont(string fontFamilyName, string localFontFaceName, FontFaceDescriptors descriptors = default, IReadOnlyList<RuneInterval>? unicodeRanges = null) => throw new NotSupportedException();
        }

        [Fact]
        public void GetSolidBrush_ReturnsASolidBrushCarryingTheColor()
        {
            var ctx = new TestRenderContext();
            var color = PaintColor.FromArgb(255, 10, 20, 30);

            var brush = Assert.IsType<SolidBrush>(ctx.GetSolidBrush(color));

            Assert.Equal(color, brush.PaintColor);
        }

        [Fact]
        public void GetSolidBrush_CachesByColor()
        {
            var ctx = new TestRenderContext();
            var color = PaintColor.FromArgb(255, 10, 20, 30);

            Assert.Same(ctx.GetSolidBrush(color), ctx.GetSolidBrush(color));
        }

        [Fact]
        public void GetPen_ForAColor_PaintsWithASolidBrushOfThatColor()
        {
            var ctx = new TestRenderContext();
            var color = PaintColor.FromArgb(255, 1, 2, 3);

            var pen = ctx.GetPen(color);

            var solid = Assert.IsType<SolidBrush>(pen.Paint);
            Assert.Equal(color, solid.PaintColor);
        }

        [Fact]
        public void GetPen_ForABrush_PaintsWithThatExactBrushInstance()
        {
            var ctx = new TestRenderContext();
            var brush = ctx.GetSolidBrush(PaintColor.Black);

            var pen = ctx.GetPen(brush);

            Assert.Same(brush, pen.Paint);
        }

        [Fact]
        public void GetLinearGradientBrush_MultiStop_CarriesThePointsAndStopsInOrder()
        {
            var ctx = new TestRenderContext();
            var p1 = new PaintPoint(0, 0);
            var p2 = new PaintPoint(100, 0);
            var stops = new (PaintColor PaintColor, double Position)[]
            {
                (PaintColor.Black, 0),
                (PaintColor.White, 1),
            };

            var brush = Assert.IsType<LinearGradientBrush>(ctx.GetLinearGradientBrush(p1, p2, stops));

            Assert.Equal(p1, brush.Start);
            Assert.Equal(p2, brush.End);
            Assert.Equal(2, brush.Stops.Count);
            Assert.Equal(PaintColor.Black, brush.Stops[0].PaintColor);
            Assert.Equal(0, brush.Stops[0].Position);
            Assert.Equal(PaintColor.White, brush.Stops[1].PaintColor);
            Assert.Equal(1, brush.Stops[1].Position);
            Assert.Equal(GradientSpread.Pad, brush.Spread);
        }

        [Fact]
        public void GetLinearGradientBrush_Repeating_UsesRepeatSpread()
        {
            var ctx = new TestRenderContext();
            var stops = new (PaintColor PaintColor, double Position)[] { (PaintColor.Black, 0), (PaintColor.White, 1) };

            var brush = Assert.IsType<LinearGradientBrush>(
                ctx.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(1, 0), stops, isRepeating: true));

            Assert.Equal(GradientSpread.Repeat, brush.Spread);
        }

        [Fact]
        public void GetLinearGradientBrush_MixingCmykAndRgbStops_Throws()
        {
            var ctx = new TestRenderContext();
            var stops = new (PaintColor PaintColor, double Position)[]
            {
                (PaintColor.FromCmyk(255, 0, 0, 0, 1), 0),
                (PaintColor.FromArgb(255, 255, 0, 0), 1),
            };

            Assert.Throws<NotSupportedException>(() => ctx.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(1, 0), stops));
        }

        [Fact]
        public void GetRadialGradientBrush_DefaultsFocalCenterToTheCenter()
        {
            var ctx = new TestRenderContext();
            var center = new PaintPoint(50, 50);
            var stops = new (PaintColor PaintColor, double Position)[] { (PaintColor.Black, 0), (PaintColor.White, 1) };

            var brush = Assert.IsType<RadialGradientBrush>(ctx.GetRadialGradientBrush(center, 10, 20, stops));

            Assert.Equal(center, brush.Center);
            Assert.Equal(center, brush.Focus);
            Assert.Equal(10, brush.RadiusX);
            Assert.Equal(20, brush.RadiusY);
        }

        [Fact]
        public void GetRadialGradientBrush_AnExplicitFocalCenter_IsUsedInsteadOfTheCenter()
        {
            var ctx = new TestRenderContext();
            var center = new PaintPoint(50, 50);
            var focus = new PaintPoint(40, 45);
            var stops = new (PaintColor PaintColor, double Position)[] { (PaintColor.Black, 0), (PaintColor.White, 1) };

            var brush = Assert.IsType<RadialGradientBrush>(ctx.GetRadialGradientBrush(center, 10, 10, stops, focalCenter: focus));

            Assert.Equal(focus, brush.Focus);
        }

        [Fact]
        public void GetConicGradientBrush_CarriesColorsAndAngles()
        {
            var ctx = new TestRenderContext();
            var center = new PaintPoint(0, 0);
            var colors = new[] { PaintColor.Black, PaintColor.White };
            var angles = new[] { 0.0, Math.PI };

            var brush = Assert.IsType<ConicGradientBrush>(ctx.GetConicGradientBrush(center, 100, colors, angles));

            Assert.Equal(center, brush.Center);
            Assert.Equal(100, brush.OuterRadius);
            Assert.Equal(2, brush.Stops.Count);
            Assert.Equal(PaintColor.Black, brush.Stops[0].PaintColor);
            Assert.Equal(PaintColor.White, brush.Stops[1].PaintColor);
        }
    }
}
