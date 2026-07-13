using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class SvgIntegrationTests
    {
        private static async Task<string> GetPdfText(string html)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        private static string InlineSvgHtml() =>
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }}</style></head><body>{SvgTestFixture.Markup}</body></html>";

        private static string ImgSvgHtml()
        {
            var dataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(SvgTestFixture.Markup));
            return $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }}</style></head><body><img src=\"{dataUri}\" width=\"200\" height=\"200\"/></body></html>";
        }

        [Fact]
        public async Task InlineSvg_RendersPathCurveOperator()
        {
            var pdfText = await GetPdfText(InlineSvgHtml());

            // Cubic Bezier curve operator, confirming the path's "C"/"A" segments made it into the
            // content stream as real vector path construction (not rasterized).
            Assert.Contains(" c\n", pdfText);
        }

        [Fact]
        public async Task InlineSvg_RendersFillAndStrokeOperators()
        {
            var pdfText = await GetPdfText(InlineSvgHtml());

            Assert.Contains("\nf\n", pdfText);
            Assert.Contains("\nS\n", pdfText);
        }

        [Fact]
        public async Task InlineSvg_RendersGradientShading()
        {
            var pdfText = await GetPdfText(InlineSvgHtml());

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task InlineSvg_RendersClipOperator()
        {
            var pdfText = await GetPdfText(InlineSvgHtml());

            // The circular clipPath applied to the badge background.
            Assert.Contains("\nW n\n", pdfText);
        }

        [Fact]
        public async Task InlineSvg_ProducesNonEmptyOutput()
        {
            var pdfText = await GetPdfText(InlineSvgHtml());

            Assert.True(pdfText.Length > 500);
        }

        [Fact]
        public async Task ImgSvg_RendersPathCurveOperator()
        {
            var pdfText = await GetPdfText(ImgSvgHtml());

            Assert.Contains(" c\n", pdfText);
        }

        [Fact]
        public async Task ImgSvg_RendersFillAndStrokeOperators()
        {
            var pdfText = await GetPdfText(ImgSvgHtml());

            Assert.Contains("\nf\n", pdfText);
            Assert.Contains("\nS\n", pdfText);
        }

        [Fact]
        public async Task ImgSvg_RendersGradientShading()
        {
            var pdfText = await GetPdfText(ImgSvgHtml());

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ImgSvg_RendersClipOperator()
        {
            var pdfText = await GetPdfText(ImgSvgHtml());

            Assert.Contains("\nW n\n", pdfText);
        }

        [Fact]
        public async Task ImgSvg_DoesNotEmbedRasterImageXObject()
        {
            var pdfText = await GetPdfText(ImgSvgHtml());

            // The SVG must be drawn as native vector paths, not rasterized into an embedded bitmap
            // XObject (the "/ProcSet [.../ImageB/ImageC/ImageI]" resource declaration is always
            // present regardless, so check for an actual image XObject's /Subtype instead).
            Assert.DoesNotContain("/Subtype /Image", pdfText);
            Assert.DoesNotContain("/Subtype/Image", pdfText);
        }

        [Fact]
        public async Task InlineSvg_UseAppliesOwnFillToUnstyledTarget()
        {
            // The <use> element's own fill/stroke become the inherited defaults for the (otherwise
            // unstyled) referenced shape - regression test for a bug where the referenced <circle>
            // (no fill of its own) rendered with the SVG-wide default black fill instead of the
            // <use>'s "none" fill + red stroke.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs><circle id="ring" cx="50" cy="50" r="40"/></defs>
                  <use xlink:href="#ring" fill="none" stroke="#c0392b" stroke-width="6"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            // Stroke color (0xC0,0x39,0x2B -> 0.753/0.224/0.169) must appear; pure black fill (the
            // wrong, pre-fix behavior) must not.
            Assert.Contains("0.753", pdfText);
            Assert.DoesNotContain("0 0 0 rg", pdfText);
        }

        [Fact]
        public async Task InlineSvg_FillRuleEvenOdd_RendersEvenOddFillOperator()
        {
            // A self-intersecting star filled with fill-rule="evenodd" must use the PDF "f*" (even-odd
            // fill) operator rather than the default "f" (nonzero winding) operator - this is the only
            // observable difference in the content stream since both rules draw the same path geometry.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <path fill-rule="evenodd" fill="#000000"
                        d="M50,5 L61,39 L98,39 L68,60 L79,95 L50,74 L21,95 L32,60 L2,39 L39,39 Z"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("f*\n", pdfText);
        }

        [Fact]
        public async Task InlineSvg_FillOpacity_AppliesRealPdfTransparency()
        {
            // fill-opacity must independently reduce fill alpha via a real PDF ExtGState "/ca" entry
            // (not just parsed-and-ignored), distinct from the shape's own group opacity.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <circle cx="50" cy="50" r="40" fill="#ff0000" fill-opacity="0.5" stroke="none"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/ca 0.5", pdfText);
        }

        [Fact]
        public async Task InlineSvg_StrokeDasharray_RendersPdfDashOperator()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <path d="M10,10 L90,10" stroke="#000000" stroke-width="1" stroke-dasharray="4,2"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("[4 2]", pdfText);
            Assert.Contains(" d\n", pdfText);
        }

        [Fact]
        public async Task InlineSvg_RotateTransform_ProducesNonAxisAlignedMatrix()
        {
            // Before Phase 2, rotate() was parsed but contributed no transform at all - the group's
            // "cm" matrix would have been the identity-derived viewBox scale only. A 30-degree
            // rotation must now push a matrix whose off-diagonal components carry cos(30)=0.866 and
            // sin(30)=0.5, which is otherwise not producible by any translate/scale-only content on
            // this page.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <g transform="rotate(30)">
                    <path d="M10,10 L90,10" stroke="#000000" stroke-width="2"/>
                  </g>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("0.866", pdfText);
            Assert.Contains("0.5", pdfText);
        }

        [Theory]
        [InlineData("""<rect x="10" y="10" width="60" height="60" fill="#000000"/>""")]
        [InlineData("""<ellipse cx="50" cy="50" rx="30" ry="20" fill="#000000"/>""")]
        [InlineData("""<polyline points="10,10 50,50 90,10" fill="none" stroke="#000000"/>""")]
        public async Task InlineSvg_BasicShapes_RenderFillOrStrokeOperator(string shapeMarkup)
        {
            var html = $"""
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  {shapeMarkup}
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.True(pdfText.Contains("\nf\n") || pdfText.Contains("\nS\n"));
        }

        [Fact]
        public async Task InlineSvg_RoundedRect_RendersCurveOperator()
        {
            // A rounded corner is only representable as a Bezier curve approximation of the arc, so a
            // "c" operator distinguishes it from a plain (straight-edged) rect.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <rect x="10" y="10" width="80" height="80" rx="10" ry="10" fill="#000000"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains(" c\n", pdfText);
        }

        [Fact]
        public async Task InlineSvg_Line_RendersStrokeButNotFillOperator()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <line x1="10" y1="10" x2="90" y2="90" stroke="#000000" stroke-width="2"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("\nS\n", pdfText);
            Assert.DoesNotContain("\nf\n", pdfText);
        }

        [Fact]
        public async Task ImgSvg_BasicShapes_RenderAsVectorNotRaster()
        {
            var dataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(
                """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100"><rect x="10" y="10" width="80" height="80" fill="#000000"/></svg>"""));
            var html = $"""<!DOCTYPE html><html><body><img src="{dataUri}" width="100" height="100"/></body></html>""";

            var pdfText = await GetPdfText(html);

            Assert.Contains("\nf\n", pdfText);
            Assert.DoesNotContain("/Subtype /Image", pdfText);
            Assert.DoesNotContain("/Subtype/Image", pdfText);
        }

        [Fact]
        public async Task InlineSvg_SymbolViaUse_RendersFillOperator()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs><symbol id="icon" viewBox="0 0 10 10"><circle cx="5" cy="5" r="5" fill="#000000"/></symbol></defs>
                  <use xlink:href="#icon" x="10" y="10" width="50" height="50"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("\nf\n", pdfText);
        }

        [Fact]
        public async Task InlineSvg_NestedSvg_RendersItsOwnContent()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <svg x="10" y="10" width="50" height="50" viewBox="0 0 10 10">
                    <rect x="0" y="0" width="10" height="10" fill="#000000"/>
                  </svg>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("\nf\n", pdfText);
        }

        [Fact]
        public async Task InlineSvg_PreserveAspectRatioNone_StretchesIndependently()
        {
            // A 200x100 viewBox stretched via preserveAspectRatio="none" into a 100x100 square viewport
            // must scale x and y independently (0.5 and 1.0), unlike the default meet mode which would
            // pick one uniform scale (0.5) for both axes - so the y-scale component of the pushed "cm"
            // matrix must show 1, not 0.5.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 200 100" width="100" height="100" preserveAspectRatio="none">
                  <rect x="0" y="0" width="200" height="100" fill="#000000"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            // The off-diagonal components print as "-0" rather than "0" here: they're the product of
            // a literal 0 in the SVG-local matrix with the page's own negative Y-flip scale further up
            // the transform stack, and IEEE 754 float multiplication propagates the sign of that
            // negative factor through a zero product - a harmless, pre-existing formatting quirk
            // unrelated to this feature, not a bug in the SVG transform math itself.
            Assert.Contains("0.5 -0 -0 1 ", pdfText);
        }

        [Fact]
        public async Task InlineSvg_ObjectBoundingBoxGradient_RendersShading()
        {
            // No gradientUnits attribute -> defaults to objectBoundingBox, resolved against the
            // rect's own 60x60 bounding box at paint time.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs><linearGradient id="g"><stop offset="0" stop-color="#000000"/><stop offset="1" stop-color="#ffffff"/></linearGradient></defs>
                  <rect x="20" y="20" width="60" height="60" fill="url(#g)"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task InlineSvg_GradientStroke_RendersShadingNotFlatColor()
        {
            // Before Phase 5, a gradient-paint stroke was approximated with the first stop's flat
            // color; it must now reference a real shading/pattern like a gradient fill does.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs><linearGradient id="g" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="100" y2="0">
                    <stop offset="0" stop-color="#000000"/><stop offset="1" stop-color="#ffffff"/>
                  </linearGradient></defs>
                  <path d="M10,50 L90,50" stroke="url(#g)" stroke-width="4" fill="none"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task InlineSvg_RadialGradientFocalPoint_OffsetsFromCenter()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs><radialGradient id="g" gradientUnits="userSpaceOnUse" cx="50" cy="50" r="40" fx="30" fy="50">
                    <stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#000000"/>
                  </radialGradient></defs>
                  <rect x="0" y="0" width="100" height="100" fill="url(#g)"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            // The focal point (fx=30) must appear as the shading's r0 circle center, distinct from
            // the outer circle's center (cx=50) - both values appear in the /Coords array.
            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("30", pdfText);
        }

        [Fact]
        public async Task InlineSvg_CurrentColor_ResolvesToCssColorOfAncestor()
        {
            var html = """
                <!DOCTYPE html><html><body style="color: #123456">
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <circle cx="50" cy="50" r="40" fill="currentColor"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            // #123456 -> (0.071, 0.204, 0.337)
            Assert.Contains("0.071", pdfText);
        }

        [Fact]
        public async Task InlineSvg_StyleAttribute_OverridesPresentationAttribute()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <rect x="10" y="10" width="80" height="80" fill="#ff0000" style="fill: #00ff00"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            // #00ff00 -> 0 1 0 rg (green); the presentation attribute's red must not appear.
            Assert.Contains("0 1 0 rg", pdfText);
            Assert.DoesNotContain("1 0 0 rg", pdfText);
        }

        [Fact]
        public async Task ImgSvg_StyleElement_ClassSelectorAppliesToShape()
        {
            // Standalone/<img> SVG (parsed as real XML via XElementSvgSourceNode), not inline <svg> -
            // see the note on SvgTreeBuilderTests.StyleElement_ClassSelector_AppliesToMatchingShape_FromCssBoxTree
            // for why the inline-<svg> equivalent of this test isn't exercised at the full-PDF level.
            var svg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100"><style>.highlight { fill: #00ff00; }</style><rect class="highlight" x="10" y="10" width="80" height="80"/></svg>""";
            var dataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
            var html = $"""<!DOCTYPE html><html><body><img src="{dataUri}" width="100" height="100"/></body></html>""";

            var pdfText = await GetPdfText(html);

            Assert.Contains("0 1 0 rg", pdfText);
        }

        [Fact]
        public async Task InlineSvg_AnchorWithExternalHref_RendersWebLinkAnnotation()
        {
            // Link annotations live in the page's annotation dictionary, not the content stream - this
            // is the first SVG integration test that needs to inspect PDF *objects* rather than content
            // stream operators, hence asserting on the object dictionary tokens directly (the PDF is
            // written in the same human-readable, uncompressed-object form the other tests already rely
            // on via CompressContentStreams=false, which only affects stream *data*, not object dicts).
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <a href="https://example.com/target">
                    <rect x="10" y="10" width="80" height="80" fill="#000000"/>
                  </a>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/Subtype /Link", pdfText);
            Assert.Contains("/S/URI", pdfText);
            Assert.Contains("https://example.com/target", pdfText);
        }

        [Fact]
        public async Task InlineSvg_AnchorWithSamePageHref_RendersGoToLinkAnnotation()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <p id="target">Destination paragraph</p>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <a href="#target">
                    <rect x="10" y="10" width="80" height="80" fill="#000000"/>
                  </a>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/Subtype /Link", pdfText);
            Assert.Contains("/GoTo", pdfText);
        }

        [Fact]
        public async Task ImgSvg_AnchorWithExternalHref_RendersWebLinkAnnotation()
        {
            var svg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100"><a href="https://example.com/target"><rect x="10" y="10" width="80" height="80" fill="#000000"/></a></svg>""";
            var dataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
            var html = $"""<!DOCTYPE html><html><body><img src="{dataUri}" width="100" height="100"/></body></html>""";

            var pdfText = await GetPdfText(html);

            Assert.Contains("/Subtype /Link", pdfText);
            Assert.Contains("https://example.com/target", pdfText);
        }

        [Fact]
        public async Task InlineSvg_AnchorWithoutHref_RendersContentButNoLinkAnnotation()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <a>
                    <rect x="10" y="10" width="80" height="80" fill="#000000"/>
                  </a>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("\nf\n", pdfText);
            Assert.DoesNotContain("/Subtype /Link", pdfText);
        }

        [Fact]
        public async Task InlineSvg_GroupFillInheritedByUnstyledChild()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <g fill="#2ecc71">
                    <path d="M50,10 L90,90 L10,90 Z"/>
                  </g>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            // #2ecc71 -> (0.180, 0.800, 0.443)
            Assert.Contains("0.18", pdfText);
            Assert.Contains("0.8", pdfText);
        }
    }
}
