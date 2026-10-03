using PeachPDF.Adapters;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace PeachPDF.Tests.Svg
{
    /// <summary>SVG 1.1 §11.2 <c>icc-color()</c>/<c>&lt;color-profile&gt;</c> and §11.9.1 <c>color-interpolation</c> on gradients.</summary>
    public class SvgColorManagementTests
    {
        private static readonly PdfSharpAdapter Adapter = new();

        private static SvgDocument BuildFrom(string markup)
        {
            var root = XDocument.Parse(markup).Root!;
            return SvgTreeBuilder.Build(new XElementSvgSourceNode(root), Adapter);
        }

        private static string DataUri(byte[] bytes) => "data:application/vnd.iccprofile;base64," + Convert.ToBase64String(bytes);

        private static string Svg(string body, byte[]? profile = null, string name = "prof") =>
            "<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' width='10' height='10'>" +
            (profile is null ? "" : $"<color-profile name='{name}' xlink:href='{DataUri(profile)}'/>") + body + "</svg>";

        private static PeachDrawing.Core.PaintColor FillOf(SvgDocument document) =>
            Assert.IsAssignableFrom<SvgElement>(document.Children.Single()).Fill.PaintColor;

        [Fact]
        public void IccColor_ResolvesThroughTheRegisteredProfile_NotTheFallback()
        {
            // A linear-TRC RGB profile: device 0.5 is mid linear light, which is well above sRGB 128.
            var document = BuildFrom(Svg("<rect width='5' height='5' fill='#ff0000 icc-color(prof, 0.5, 0.5, 0.5)'/>", IccProfileFixture.BuildRgbProfile()));

            var color = FillOf(document);
            Assert.NotEqual(255, color.R);
            Assert.InRange(color.R, 150, 230);
            Assert.Equal(color.R, color.G);
            Assert.Equal(color.R, color.B);
        }

        [Fact]
        public void IccColor_ProfileNameIsCaseInsensitive()
        {
            var document = BuildFrom(Svg("<rect width='5' height='5' fill='#ff0000 icc-color(PROF, 0.5, 0.5, 0.5)'/>", IccProfileFixture.BuildRgbProfile()));

            Assert.NotEqual(255, FillOf(document).R);
        }

        [Fact]
        public void IccColor_GrayProfile_TakesOneComponent()
        {
            var document = BuildFrom(Svg("<rect width='5' height='5' fill='#ff0000 icc-color(prof, 0.5)'/>", IccProfileFixture.BuildGrayProfile()));

            var color = FillOf(document);
            Assert.NotEqual(255, color.R);
            Assert.Equal(color.R, color.G);
        }

        [Theory]
        [InlineData("#ff0000 icc-color(nope, 0.5, 0.5, 0.5)")]   // unknown profile
        [InlineData("#ff0000 icc-color(prof, 0.5, 0.5)")]        // wrong component count
        [InlineData("#ff0000 icc-color(prof, a, b, c)")]         // not numbers
        [InlineData("#ff0000 icc-color(prof, 0.5, 0.5, 0.5")]    // unterminated
        public void IccColor_UnusableForm_UsesTheSrgbFallback(string fill)
        {
            var document = BuildFrom(Svg($"<rect width='5' height='5' fill='{fill}'/>", IccProfileFixture.BuildRgbProfile()));

            var color = FillOf(document);
            Assert.Equal((255, 0, 0), (color.R, color.G, color.B));
        }

        [Fact]
        public void IccColor_NoColorProfileElement_UsesTheSrgbFallback()
        {
            var document = BuildFrom(Svg("<rect width='5' height='5' fill='#00ff00 icc-color(prof, 0.5, 0.5, 0.5)'/>"));

            var color = FillOf(document);
            Assert.Equal((0, 255, 0), (color.R, color.G, color.B));
        }

        [Fact]
        public void IccColor_AppliesToStrokeAndStopColor()
        {
            var document = BuildFrom(Svg(
                "<linearGradient id='g'><stop offset='0' stop-color='#ff0000 icc-color(prof, 0.5, 0.5, 0.5)'/><stop offset='1' style='stop-color:#ff0000 icc-color(prof, 0.5, 0.5, 0.5)'/></linearGradient>" +
                "<rect width='5' height='5' stroke='#ff0000 icc-color(prof, 0.5, 0.5, 0.5)'/>",
                IccProfileFixture.BuildRgbProfile()));

            Assert.NotEqual(255, document.Gradients["g"].Stops[0].PaintColor.R);
            Assert.NotEqual(255, document.Gradients["g"].Stops[1].PaintColor.R);
            Assert.NotEqual(255, document.Children.Single().Stroke.PaintColor.R);
        }

        [Fact]
        public void IccColor_ProfileDefinedAfterUse_StillResolves()
        {
            var document = BuildFrom(
                "<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' width='10' height='10'>" +
                "<rect width='5' height='5' fill='#ff0000 icc-color(prof, 0.5, 0.5, 0.5)'/>" +
                $"<color-profile name='prof' xlink:href='{DataUri(IccProfileFixture.BuildRgbProfile())}'/></svg>");

            Assert.NotEqual(255, FillOf(document).R);
        }

        [Fact]
        public void IccColor_ProfileFromAPrefetchedResource_Resolves()
        {
            var root = XDocument.Parse(
                "<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><color-profile name='prof' href='profiles/p.icc'/>" +
                "<rect width='5' height='5' fill='#ff0000 icc-color(prof, 0.5, 0.5, 0.5)'/></svg>").Root!;
            var prefetched = new Dictionary<string, SvgTreeBuilder.SvgImageResource>
            {
                ["profiles/p.icc"] = new(IccProfileFixture.BuildRgbProfile(), IsSvg: false),
            };

            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(root), Adapter, prefetchedImages: prefetched);

            Assert.NotEqual(255, FillOf(document).R);
        }

        [Fact]
        public void IccColor_UnparseableProfileBytes_UsesTheSrgbFallback()
        {
            var document = BuildFrom(Svg("<rect width='5' height='5' fill='#ff0000 icc-color(prof, 0.5, 0.5, 0.5)'/>", [1, 2, 3]));

            Assert.Equal(255, FillOf(document).R);
        }

        [Theory]
        [InlineData("perceptual")]
        [InlineData("relative-colorimetric")]
        [InlineData("saturation")]
        [InlineData("absolute-colorimetric")]
        [InlineData("auto")]
        [InlineData("bogus")]
        public void IccColor_RenderingIntentAttribute_IsAccepted(string intent)
        {
            var markup = Svg("<rect width='5' height='5' fill='#ff0000 icc-color(prof, 0.5, 0.5, 0.5)'/>", IccProfileFixture.BuildRgbProfile())
                .Replace("<color-profile ", $"<color-profile rendering-intent='{intent}' ");

            // An intent the profile cannot honor falls back to the sRGB color instead of failing the render.
            Assert.True(FillOf(BuildFrom(markup)).R > 0);
        }

        [Fact]
        public void Rewrite_ValueWithoutIccColor_IsUnchanged()
        {
            Assert.Equal("#abc", SvgIccColor.Rewrite("#abc", null));
            Assert.Null(SvgIccColor.Rewrite(null, null));
        }

        [Fact]
        public void Rewrite_FallbackThatItselfHasParens_IsPreserved()
        {
            Assert.Equal("rgb(1, 2, 3)", SvgIccColor.Rewrite("rgb(1, 2, 3) icc-color(x, 1)", new Dictionary<string, SvgIccColor>()));
        }

        [Fact]
        public void ColorInterpolation_DefaultAndSrgb_KeepTheAuthoredStops()
        {
            const string stops = "<stop offset='0' stop-color='#000'/><stop offset='1' stop-color='#fff'/>";

            Assert.Equal(2, BuildFrom(Svg($"<linearGradient id='g'>{stops}</linearGradient>")).Gradients["g"].Stops.Count);
            Assert.Equal(2, BuildFrom(Svg($"<linearGradient id='g' color-interpolation='sRGB'>{stops}</linearGradient>")).Gradients["g"].Stops.Count);
            Assert.Equal(2, BuildFrom(Svg($"<linearGradient id='g' color-interpolation='auto'>{stops}</linearGradient>")).Gradients["g"].Stops.Count);
        }

        [Fact]
        public void ColorInterpolation_LinearRgb_SamplesStopsThroughLinearLight()
        {
            var stops = BuildFrom(Svg(
                "<linearGradient id='g' color-interpolation='linearRGB'><stop offset='0' stop-color='#000'/><stop offset='1' stop-color='#fff'/></linearGradient>"))
                .Gradients["g"].Stops;

            Assert.True(stops.Count > 2);
            Assert.Equal(0, stops[0].Offset);
            Assert.Equal(1, stops[^1].Offset);
            Assert.All(stops.Zip(stops.Skip(1)), pair => Assert.True(pair.Second.Offset >= pair.First.Offset));

            // Halfway through linear light (0.5 linear) is sRGB ~188, not the sRGB midpoint 128.
            var middle = stops.OrderBy(s => Math.Abs(s.Offset - 0.5)).First();
            Assert.InRange(middle.PaintColor.R, 170, 205);
        }

        [Fact]
        public void ColorInterpolation_IsInheritedFromAnAncestor_AndOverridable()
        {
            const string stops = "<stop offset='0' stop-color='#000'/><stop offset='1' stop-color='#fff'/>";
            var inherited = BuildFrom(Svg($"<g color-interpolation='linearRGB'><linearGradient id='g'>{stops}</linearGradient></g>"));
            var overridden = BuildFrom(Svg($"<g color-interpolation='linearRGB'><linearGradient id='g' color-interpolation='sRGB'>{stops}</linearGradient></g>"));
            var explicitInherit = BuildFrom(Svg($"<g color-interpolation='linearRGB'><linearGradient id='g' color-interpolation='inherit'>{stops}</linearGradient></g>"));

            Assert.True(inherited.Gradients["g"].Stops.Count > 2);
            Assert.Equal(2, overridden.Gradients["g"].Stops.Count);
            Assert.True(explicitInherit.Gradients["g"].Stops.Count > 2);
        }

        [Fact]
        public void ColorInterpolation_CoincidentStops_AreNotSubdivided()
        {
            var stops = BuildFrom(Svg(
                "<radialGradient id='g' color-interpolation='linearRGB'><stop offset='0.5' stop-color='#000'/><stop offset='0.5' stop-color='#fff'/></radialGradient>"))
                .Gradients["g"].Stops;

            Assert.Equal(2, stops.Count);
        }

        private const string MaskedRect = "<rect width='8' height='8' fill='red' mask='url(#m)'/>";

        private static SvgMask MaskOf(SvgDocument document) => document.Masks["m"];

        [Fact]
        public void MaskColorInterpolation_Default_KeepsTheContentColors()
        {
            var document = BuildFrom(Svg($"<mask id='m'><rect width='8' height='8' fill='#808080'/></mask>{MaskedRect}"));

            Assert.Equal(128, MaskOf(document).Children.Single().Fill.PaintColor.R);
        }

        [Fact]
        public void MaskColorInterpolation_LinearRgb_ConvertsContentToLinearLight()
        {
            var document = BuildFrom(Svg($"<mask id='m' color-interpolation='linearRGB'><rect width='8' height='8' fill='#808080' stroke='#ffffff'/></mask>{MaskedRect}"));

            var rect = MaskOf(document).Children.Single();
            Assert.Equal(55, rect.Fill.PaintColor.R);      // sRGB 128 is 0.2158 in linear light
            Assert.Equal(255, rect.Stroke.PaintColor.R);   // white stays white
        }

        [Fact]
        public void MaskColorInterpolation_LinearRgb_ReachesNestedContentAndKeepsAlpha()
        {
            var document = BuildFrom(Svg($"<mask id='m' color-interpolation='linearRGB'><g><rect width='8' height='8' fill='rgba(128,128,128,0.5)'/></g></mask>{MaskedRect}"));

            var nested = Assert.IsType<SvgGroupElement>(MaskOf(document).Children.Single()).Children.Single();
            Assert.Equal(55, nested.Fill.PaintColor.R);
            Assert.InRange(nested.Fill.PaintColor.A, 126, 129);
        }

        [Fact]
        public void MaskColorInterpolation_LinearRgb_CopiesTheReferencedGradientAndLeavesTheOriginal()
        {
            var document = BuildFrom(Svg(
                "<linearGradient id='g'><stop offset='0' stop-color='#808080'/><stop offset='1' stop-color='#fff'/></linearGradient>" +
                "<mask id='m' color-interpolation='linearRGB'><rect width='8' height='8' fill='url(#g)'/></mask>" +
                "<rect width='8' height='8' fill='url(#g)' mask='url(#m)'/>"));

            var paint = MaskOf(document).Children.Single().Fill;
            Assert.NotEqual("g", paint.ReferenceId);
            Assert.Equal(55, document.Gradients[paint.ReferenceId!].Stops[0].PaintColor.R);
            Assert.Equal(128, document.Gradients["g"].Stops[0].PaintColor.R);
        }

        [Fact]
        public void MaskColorInterpolation_LinearRgb_CopiesARadialGradient()
        {
            var document = BuildFrom(Svg(
                "<mask id='m' color-interpolation='linearRGB'><rect width='8' height='8' fill='url(#r)'/></mask>" +
                "<radialGradient id='r' cx='0.3' cy='0.4' r='0.6' fx='0.2' fy='0.1'><stop offset='0' stop-color='#808080'/><stop offset='1' stop-color='#000'/></radialGradient>" +
                MaskedRect));

            var clone = Assert.IsType<SvgRadialGradient>(document.Gradients[MaskOf(document).Children.Single().Fill.ReferenceId!]);
            Assert.Equal((0.3, 0.4, 0.6, 0.2, 0.1), (clone.Cx, clone.Cy, clone.R, clone.Fx, clone.Fy));
            Assert.Equal(55, clone.Stops[0].PaintColor.R);
        }
    }
}
