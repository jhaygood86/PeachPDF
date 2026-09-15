using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using System;
using Xunit;

namespace PeachPDF.Tests.Adapters
{
    /// <summary>
    /// <c>PdfSharpAdapter.RejectMixedColorSpaceGradientStops</c>: a gradient mixing <c>device-cmyk()</c>
    /// and RGB-authored stops is rejected outright rather than risking a component-count mismatch between
    /// a shading's <c>/ColorSpace</c> and its <c>/C0</c>/<c>/C1</c> function output (see the guard's own
    /// doc comment) - no defined conversion exists between the two spaces. A gradient whose stops are all
    /// CMYK (or all RGB) has no such conflict and is fully supported - see
    /// <c>DeviceCmykGradientIntegrationTests</c> for the structural PDF-content assertions that prove that
    /// case actually produces a correct <c>/DeviceCMYK</c> shading, not just that it doesn't throw.
    /// Exercised directly against the public <see cref="RAdapter"/> gradient-brush entry points rather than
    /// through HTML, since the legacy 2-color <c>GetLinearGradientBrush(RRect, RColor, RColor, double)</c>
    /// overload has no CSS/SVG caller at all today (confirmed by inspection - every real gradient goes
    /// through the multi-stop overloads) and so is otherwise unreachable from an integration test.
    /// </summary>
    public class PdfSharpAdapterCmykGradientTests
    {
        private static readonly RColor CmykStop = RColor.FromCmyk(255, 0f, 1f, 1f, 0f);
        private static readonly RColor CmykStop2 = RColor.FromCmyk(255, 1f, 0f, 0f, 0f);
        private static readonly RColor RgbStop = RColor.FromArgb(255, 0, 0);

        [Fact]
        public void LegacyTwoColorLinearGradient_CmykColor1_Throws()
        {
            var adapter = new PdfSharpAdapter();
            Assert.Throws<NotSupportedException>(() =>
                adapter.GetLinearGradientBrush(new RRect(0, 0, 10, 10), CmykStop, RgbStop, 0));
        }

        [Fact]
        public void LegacyTwoColorLinearGradient_CmykColor2_Throws()
        {
            var adapter = new PdfSharpAdapter();
            Assert.Throws<NotSupportedException>(() =>
                adapter.GetLinearGradientBrush(new RRect(0, 0, 10, 10), RgbStop, CmykStop, 0));
        }

        [Fact]
        public void LegacyTwoColorLinearGradient_BothRgb_Succeeds()
        {
            var adapter = new PdfSharpAdapter();
            var brush = adapter.GetLinearGradientBrush(new RRect(0, 0, 10, 10), RgbStop, RgbStop, 0);
            Assert.NotNull(brush);
        }

        [Fact]
        public void LegacyTwoColorLinearGradient_BothCmyk_Succeeds()
        {
            var adapter = new PdfSharpAdapter();
            var brush = adapter.GetLinearGradientBrush(new RRect(0, 0, 10, 10), CmykStop, CmykStop2, 0);
            Assert.NotNull(brush);
        }

        [Fact]
        public void MultiStopLinearGradient_MixedStop_Throws()
        {
            var adapter = new PdfSharpAdapter();
            var stops = new (RColor Color, double Position)[] { (RgbStop, 0), (CmykStop, 1) };
            Assert.Throws<NotSupportedException>(() =>
                adapter.GetLinearGradientBrush(new RPoint(0, 0), new RPoint(10, 10), stops));
        }

        [Fact]
        public void MultiStopLinearGradient_AllCmykStops_Succeeds()
        {
            var adapter = new PdfSharpAdapter();
            var stops = new (RColor Color, double Position)[] { (CmykStop, 0), (CmykStop2, 1) };
            var brush = adapter.GetLinearGradientBrush(new RPoint(0, 0), new RPoint(10, 10), stops);
            Assert.NotNull(brush);
        }

        [Fact]
        public void RadialGradient_MixedStop_Throws()
        {
            var adapter = new PdfSharpAdapter();
            var stops = new (RColor Color, double Position)[] { (RgbStop, 0), (CmykStop, 1) };
            Assert.Throws<NotSupportedException>(() =>
                adapter.GetRadialGradientBrush(new RPoint(5, 5), 5, 5, stops));
        }

        [Fact]
        public void RadialGradient_AllCmykStops_Succeeds()
        {
            var adapter = new PdfSharpAdapter();
            var stops = new (RColor Color, double Position)[] { (CmykStop, 0), (CmykStop2, 1) };
            var brush = adapter.GetRadialGradientBrush(new RPoint(5, 5), 5, 5, stops);
            Assert.NotNull(brush);
        }

        [Fact]
        public void ConicGradient_MixedStop_Throws()
        {
            var adapter = new PdfSharpAdapter();
            var colors = new[] { RgbStop, CmykStop };
            var angles = new[] { 0.0, Math.PI };
            Assert.Throws<NotSupportedException>(() =>
                adapter.GetConicGradientBrush(new RPoint(5, 5), 5, colors, angles));
        }

        [Fact]
        public void ConicGradient_AllCmykStops_Succeeds()
        {
            var adapter = new PdfSharpAdapter();
            var colors = new[] { CmykStop, CmykStop2 };
            var angles = new[] { 0.0, Math.PI };
            var brush = adapter.GetConicGradientBrush(new RPoint(5, 5), 5, colors, angles);
            Assert.NotNull(brush);
        }

        [Fact]
        public void ToString_CmykColor_ShowsCmykComponents()
        {
            var text = CmykStop.ToString();

            Assert.Contains("C=", text);
            Assert.Contains("M=", text);
            Assert.Contains("Y=", text);
            Assert.Contains("K=", text);
            Assert.DoesNotContain("R=", text);
        }
    }
}
