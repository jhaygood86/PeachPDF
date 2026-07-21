using PeachPDF.PdfSharpCore;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for issue #134: a brush-backed stroke pen (SVG stroke="url(#gradient)")
    /// carries no meaningful pen.Color - its constructor leaves Color at the default transparent
    /// black (alpha 0). RealizePen used pen.Color.A to drive the stroke's constant alpha /CA, so a
    /// gradient stroke emitted right after any opaque solid stroke got /CA 0 and became invisible.
    /// </summary>
    public class SvgGradientStrokeAlphaTests
    {
        private static async Task<string> GetPdfText(string svg)
        {
            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }}</style></head><body>{svg}</body></html>";
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        // The issue's minimal repro shape: an opaque solid stroke immediately followed by a gradient
        // (brush) stroke - the ordering that poisoned the realized stroke alpha to 0.
        private const string SolidThenGradientSvg =
            "<svg width=\"300\" height=\"120\" viewBox=\"0 0 300 120\" xmlns=\"http://www.w3.org/2000/svg\">" +
            "<defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\">" +
            "<stop offset=\"0\" stop-color=\"#c00\"/><stop offset=\"1\" stop-color=\"#00c\"/></linearGradient></defs>" +
            "<rect x=\"10\" y=\"10\" width=\"120\" height=\"100\" fill=\"none\" stroke=\"#0a0\" stroke-width=\"8\"/>" +
            "<ellipse cx=\"230\" cy=\"60\" rx=\"60\" ry=\"50\" fill=\"none\" stroke=\"url(#g)\" stroke-width=\"8\"/>" +
            "</svg>";

        [Fact]
        public async Task GradientStrokeAfterOpaqueStroke_DoesNotZeroTheStrokeAlpha()
        {
            var pdfText = await GetPdfText(SolidThenGradientSvg);

            // Every stroke here is fully opaque, so no ExtGState may set the constant stroke alpha to
            // zero. Parse the actual ExtGState objects and assert none carries "/CA 0" - a zeroed /CA
            // is exactly what made the gradient stroke invisible.
            foreach (Match obj in Regex.Matches(pdfText, @"\d+ 0 obj(.*?)endobj", RegexOptions.Singleline))
            {
                var body = obj.Groups[1].Value;
                if (!body.Contains("/ExtGState"))
                    continue;

                var ca = Regex.Match(body, @"/CA\s+([0-9.]+)");
                if (ca.Success)
                    Assert.NotEqual(0.0, double.Parse(ca.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        [Fact]
        public async Task GradientStrokeAfterOpaqueStroke_StillEmitsItsShadingPatternStroke()
        {
            var pdfText = await GetPdfText(SolidThenGradientSvg);

            // The opaque solid stroke establishes its DeviceRGB color, then the gradient stroke must
            // switch to the shading pattern and paint through it (SCN) - both must be present, and in
            // that order, for the trigger condition to actually be exercised.
            var solidStrokeIndex = pdfText.IndexOf(" RG\n", StringComparison.Ordinal);
            var patternStrokeIndex = pdfText.IndexOf("/Pattern CS", StringComparison.Ordinal);

            Assert.True(solidStrokeIndex >= 0, "expected the opaque solid stroke to realize a DeviceRGB stroke color");
            Assert.True(patternStrokeIndex >= 0, "expected the gradient stroke to realize a shading pattern");
            Assert.True(solidStrokeIndex < patternStrokeIndex, "the solid stroke must precede the gradient stroke to reproduce the bug");
            Assert.Contains(" SCN\n", pdfText);
            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task SolidStrokeAfterGradientStroke_ReestablishesItsOwnColor()
        {
            // A gradient stroke switches the stroke color space to /Pattern. A following solid stroke
            // must re-emit its own DeviceRGB color (RG) so it strokes in that color rather than silently
            // reusing the previous shape's shading pattern (which rendered a "black" stroke as the
            // gradient). This mirrors how the fill side already handles a solid fill after a pattern fill.
            var svg =
                "<svg width=\"300\" height=\"120\" viewBox=\"0 0 300 120\" xmlns=\"http://www.w3.org/2000/svg\">" +
                "<defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\">" +
                "<stop offset=\"0\" stop-color=\"#c00\"/><stop offset=\"1\" stop-color=\"#00c\"/></linearGradient></defs>" +
                "<ellipse cx=\"70\" cy=\"60\" rx=\"50\" ry=\"45\" fill=\"none\" stroke=\"url(#g)\" stroke-width=\"8\"/>" +
                "<rect x=\"170\" y=\"15\" width=\"110\" height=\"90\" fill=\"none\" stroke=\"#000\" stroke-width=\"8\"/>" +
                "</svg>";

            var pdfText = await GetPdfText(svg);

            // The gradient stroke's pattern is set up first...
            var patternStrokeIndex = pdfText.IndexOf("/Pattern CS", StringComparison.Ordinal);
            Assert.True(patternStrokeIndex >= 0, "expected the gradient stroke to realize a shading pattern");

            // ...and after it, the black solid stroke must re-establish a DeviceRGB stroke color (RG),
            // so it is not left stroking in the /Pattern color space.
            var solidStrokeIndex = pdfText.IndexOf(" RG\n", patternStrokeIndex, StringComparison.Ordinal);
            Assert.True(solidStrokeIndex > patternStrokeIndex,
                "a solid stroke following a gradient stroke must re-emit its RG color, not reuse the pattern");
        }

        [Fact]
        public async Task SemiTransparentGradientStrokeAfterOpaqueStroke_KeepsItsSoftMaskAlpha()
        {
            // A gradient with a translucent stop still carries real transparency via its own soft-mask
            // ExtGState (a /SMask), which must survive - the fix only stops pen.Color from driving the
            // constant /CA, it must not suppress genuine gradient transparency.
            var svg =
                "<svg width=\"300\" height=\"120\" viewBox=\"0 0 300 120\" xmlns=\"http://www.w3.org/2000/svg\">" +
                "<defs><linearGradient id=\"g\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\">" +
                "<stop offset=\"0\" stop-color=\"#c00\" stop-opacity=\"0.2\"/><stop offset=\"1\" stop-color=\"#00c\"/></linearGradient></defs>" +
                "<rect x=\"10\" y=\"10\" width=\"120\" height=\"100\" fill=\"none\" stroke=\"#0a0\" stroke-width=\"8\"/>" +
                "<ellipse cx=\"230\" cy=\"60\" rx=\"60\" ry=\"50\" fill=\"none\" stroke=\"url(#g)\" stroke-width=\"12\"/>" +
                "</svg>";

            var pdfText = await GetPdfText(svg);

            Assert.Contains("/SMask", pdfText);
            Assert.DoesNotContain("/CA 0\n", pdfText);
        }
    }
}
