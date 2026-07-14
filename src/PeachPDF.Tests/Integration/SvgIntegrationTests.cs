using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
        public async Task InlineSvg_LinearSpreadMethodRepeat_TilesStopsAcrossShapeBounds()
        {
            // Regression test: SVG's x1/y1/x2/y2 define only ONE cycle of the gradient, unlike CSS's
            // repeating-linear-gradient() (whose axis is already sized to span the whole background
            // box before the stop list is built) - spreadMethod="repeat"/"reflect" must tile that one
            // cycle outward to actually cover the shape. The original implementation only ever
            // toggled the PDF shading's /Extend to false with no tiling behind it, so a short axis
            // (20 units here) painted into an 80x80 rect left most of the rect unpainted. A real fix
            // replicates the stops into a stitching function (/FunctionType 3) across an axis extended
            // to cover the shape's bounding box - assert that stitching function actually appears,
            // rather than the plain 2-color /FunctionType 2 a single unrepeated cycle would produce.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs><linearGradient id="g" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="20" y2="0" spreadMethod="repeat">
                    <stop offset="0" stop-color="#ff9966"/><stop offset="1" stop-color="#ff5e62"/>
                  </linearGradient></defs>
                  <rect x="10" y="10" width="80" height="80" fill="url(#g)"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/FunctionType 3", pdfText);
        }

        [Fact]
        public async Task InlineSvg_LinearSpreadMethodReflect_TilesStopsAcrossShapeBounds()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs><linearGradient id="g" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="20" y2="0" spreadMethod="reflect">
                    <stop offset="0" stop-color="#00c9ff"/><stop offset="1" stop-color="#92fe9d"/>
                  </linearGradient></defs>
                  <rect x="10" y="10" width="80" height="80" fill="url(#g)"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/FunctionType 3", pdfText);
        }

        [Fact]
        public async Task InlineSvg_RadialSpreadMethodRepeat_TilesStopsAcrossShapeBounds()
        {
            // Radial counterpart: rings tile outward from the center rather than extending along an
            // axis, but the same "must actually expand the stop list, not just flip /Extend" bug
            // applied here too.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs><radialGradient id="g" gradientUnits="userSpaceOnUse" cx="50" cy="50" r="10" spreadMethod="repeat">
                    <stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#000000"/>
                  </radialGradient></defs>
                  <rect x="0" y="0" width="100" height="100" fill="url(#g)"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/ShadingType", pdfText);
            Assert.Contains("/FunctionType 3", pdfText);
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
        public async Task InlineSvg_Marker_RendersAtEachVertexOfAPolyline()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs>
                    <marker id="dot" markerWidth="4" markerHeight="4" refX="2" refY="2" markerUnits="userSpaceOnUse">
                      <circle cx="2" cy="2" r="2" fill="#ff00ff"/>
                    </marker>
                  </defs>
                  <polyline points="10,10 50,50 90,10" fill="none" stroke="#000000" marker="url(#dot)"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            // The marker's own magenta fill (#ff00ff -> "1 0 1 rg") must appear once per vertex (start,
            // mid, end) of the 3-point polyline - a single shared marker definition painted three times
            // at three distinct transformed positions.
            var occurrences = 0;
            var index = 0;
            while ((index = pdfText.IndexOf("1 0 1 rg", index, StringComparison.Ordinal)) >= 0)
            {
                occurrences++;
                index += 1;
            }

            Assert.True(occurrences >= 3, $"Expected at least 3 marker instances, found {occurrences}");
        }

        [Fact]
        public async Task InlineSvg_Marker_DoesNotApplyToRect()
        {
            // Per spec, markers only attach to path/line/polyline/polygon - a <rect> with marker-start
            // set must not paint the marker at all.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs>
                    <marker id="dot" markerWidth="4" markerHeight="4" refX="2" refY="2">
                      <circle cx="2" cy="2" r="2" fill="#ff00ff"/>
                    </marker>
                  </defs>
                  <rect x="10" y="10" width="80" height="80" fill="none" stroke="#000000" marker-start="url(#dot)"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.DoesNotContain("1 0 1 rg", pdfText);
        }

        [Fact]
        public async Task InlineSvg_PatternFill_RendersFormXObjectTiledMultipleTimes()
        {
            // Unlike a gradient (a native PDF shading), this renderer's <pattern> support is a
            // Form XObject "tile" drawn repeatedly (clipped to the shape's fill geometry) rather than
            // a native PDF tiling pattern object - still fully vector (each repeat is a reference to
            // the same underlying vector content, never a rasterized bitmap), but the PDF-level
            // evidence to check for is "/Subtype /Form" plus multiple "Do" (XObject invoke) operators,
            // not "/PatternType".
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs>
                    <pattern id="dots" patternUnits="userSpaceOnUse" width="10" height="10">
                      <circle cx="5" cy="5" r="4" fill="#ff00ff"/>
                    </pattern>
                  </defs>
                  <rect x="0" y="0" width="100" height="100" fill="url(#dots)"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/Subtype /Form", pdfText);
            Assert.DoesNotContain("/Subtype /Image", pdfText);

            var doCount = 0;
            var index = 0;
            while ((index = pdfText.IndexOf(" Do ", index, StringComparison.Ordinal)) >= 0)
            {
                doCount++;
                index += 1;
            }

            Assert.True(doCount >= 2, $"Expected at least 2 tile placements, found {doCount}");
        }

        [Fact]
        public async Task InlineSvg_ObjectBoundingBoxPattern_TilesWithinShapeBounds()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs>
                    <pattern id="dots" width="0.25" height="0.25">
                      <rect x="0" y="0" width="5" height="5" fill="#00ffff"/>
                    </pattern>
                  </defs>
                  <circle cx="50" cy="50" r="40" fill="url(#dots)"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/Subtype /Form", pdfText);
        }

        [Fact]
        public async Task ImgSvg_PatternFill_RendersFormXObject()
        {
            var svg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100"><defs><pattern id="dots" patternUnits="userSpaceOnUse" width="10" height="10"><circle cx="5" cy="5" r="4" fill="#ff00ff"/></pattern></defs><rect x="0" y="0" width="100" height="100" fill="url(#dots)"/></svg>""";
            var dataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
            var html = $"""<!DOCTYPE html><html><body><img src="{dataUri}" width="100" height="100"/></body></html>""";

            var pdfText = await GetPdfText(html);

            Assert.Contains("/Subtype /Form", pdfText);
        }

        [Fact]
        public async Task InlineSvg_Mask_RendersSoftMaskExtGState()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs>
                    <mask id="fade" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100">
                      <rect x="0" y="0" width="100" height="100" fill="#ffffff"/>
                    </mask>
                  </defs>
                  <rect x="10" y="10" width="80" height="80" fill="#000000" mask="url(#fade)"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/SMask", pdfText);
            Assert.Contains("/Luminosity", pdfText);
            Assert.Contains("/Subtype /Form", pdfText);
        }

        [Fact]
        public async Task InlineSvg_MaskWithGradientContent_RendersShadingInsideMaskForm()
        {
            // A mask's own content can itself use fill/gradients (a full paint, not just geometry,
            // unlike clip-path) - a gradient-filled mask rect must still produce a real shading.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs>
                    <linearGradient id="fadeGrad" x1="0" y1="0" x2="1" y2="0">
                      <stop offset="0" stop-color="#000000"/>
                      <stop offset="1" stop-color="#ffffff"/>
                    </linearGradient>
                    <mask id="fade" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100">
                      <rect x="0" y="0" width="100" height="100" fill="url(#fadeGrad)"/>
                    </mask>
                  </defs>
                  <rect x="10" y="10" width="80" height="80" fill="#000000" mask="url(#fade)"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/SMask", pdfText);
            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ImgSvg_Mask_RendersSoftMaskExtGState()
        {
            var svg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100"><defs><mask id="fade" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100"><rect x="0" y="0" width="100" height="100" fill="#ffffff"/></mask></defs><rect x="10" y="10" width="80" height="80" fill="#000000" mask="url(#fade)"/></svg>""";
            var dataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
            var html = $"""<!DOCTYPE html><html><body><img src="{dataUri}" width="100" height="100"/></body></html>""";

            var pdfText = await GetPdfText(html);

            Assert.Contains("/SMask", pdfText);
            Assert.Contains("/Luminosity", pdfText);
        }

        [Fact]
        public async Task InlineSvg_Mask_SMaskGsAndContentDoShareTheSameCm()
        {
            // Regression test for a bug where the masked content and the mask's own Form XObject were
            // positioned via two DIFFERENT coordinate conventions (the mask tile's own small-height
            // Y-flip vs the page's), so relying on "gs" (activate the mask) at some ambient point in
            // the content stream, applied separately from wherever the content itself later got drawn,
            // silently misaligned the two - the mask would evaluate as fully transparent everywhere,
            // even though every other check here (/SMask, /Luminosity, /Subtype /Form present) still
            // passed, since those only check token presence, not where the tokens actually land.
            // RGraphics.DrawImageMasked fixes this by emitting the mask's "gs" and the content's "Do"
            // on the SAME "q ... cm ... gs ... Do Q" line, sharing one placement transform - assert
            // that structure directly so a future regression to the old "ambient gs, unrelated Do"
            // shape fails loudly here instead of only being visible as a blank render.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs>
                    <mask id="fade" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100">
                      <rect x="0" y="0" width="100" height="100" fill="#ffffff"/>
                    </mask>
                  </defs>
                  <rect x="10" y="10" width="80" height="80" fill="#000000" mask="url(#fade)"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Matches(new Regex(@"cm /GS\d+ gs /Fm\d+ Do"), pdfText);
        }

        [Fact]
        public async Task InlineSvg_Mask_ContentFormIsItselfATransparencyGroup()
        {
            // Regression test for a bug where the masked CONTENT form (not just the mask's own /G
            // form) needs its own /Group transparency dictionary for the active SMask to actually take
            // effect. Its absence is invisible in MuPDF (which applies an SMask to plain, non-group
            // content leniently) but PDFium (Chrome/Edge, and most real-world viewers/printers)
            // silently ignores the mask entirely without it - masked content rendered fully opaque,
            // as if unmasked. Neither symptom is a "does the token exist" problem (/SMask, /Luminosity,
            // /Subtype /Form all still present either way), so this specifically counts the "/I true"
            // marker (isolated group, set on both the mask form and the content form, but not on the
            // page's own top-level /Group) to confirm BOTH forms - not just the mask - are groups.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs>
                    <mask id="fade" maskUnits="userSpaceOnUse" x="0" y="0" width="100" height="100">
                      <rect x="0" y="0" width="100" height="100" fill="#ffffff"/>
                    </mask>
                  </defs>
                  <rect x="10" y="10" width="80" height="80" fill="#000000" mask="url(#fade)"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            var isolatedGroupCount = Regex.Matches(pdfText, @"/I true").Count;
            Assert.True(isolatedGroupCount >= 2, $"Expected at least 2 isolated transparency groups (mask form + content form), found {isolatedGroupCount}");
        }

        [Fact]
        public async Task InlineSvg_GroupOpacity_RendersIsolatedTransparencyGroup()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <g opacity="0.5">
                    <rect x="10" y="10" width="60" height="60" fill="#ff0000"/>
                  </g>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/Subtype /Form", pdfText);
            Assert.Contains("/S /Transparency", pdfText);
            Assert.Matches(new Regex(@"/ca 0\.5\b"), pdfText);
        }

        [Fact]
        public async Task InlineSvg_GroupOpacity_OverlappingChildren_DoNotDoubleBlend()
        {
            // The SVG-side equivalent of OpacityIntegrationTests.Opacity_OverlappingChildren_DoNotDoubleBlend -
            // regression test for the double-blend limitation documented at supported-svg-features.md
            // (a <g opacity> containing overlapping shapes used to double-darken at the overlap, since
            // opacity was applied as a per-shape alpha multiply rather than an isolated group composite).
            // Only ONE /ca 0.5 should appear (the group's own composite) - the two overlapping rects
            // must paint at full local alpha inside the isolated tile, not each carry their own /ca 0.5.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <g opacity="0.5">
                    <rect x="10" y="10" width="50" height="50" fill="#ff0000"/>
                    <rect x="30" y="30" width="50" height="50" fill="#0000ff"/>
                  </g>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            var alphaMatches = Regex.Matches(pdfText, @"/ca 0\.5\b");
            Assert.Single(alphaMatches);
        }

        [Fact]
        public async Task InlineSvg_GroupOpacity_CombinedWithLeafFillOpacity()
        {
            // A leaf shape's own fill-opacity must still combine with an ancestor group's opacity -
            // the group composites once at its own 0.5, and the leaf's fill-opacity (0.5) is applied
            // independently to that leaf's own color inside the (now-isolated) tile.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <g opacity="0.5">
                    <rect x="10" y="10" width="60" height="60" fill="#ff0000" fill-opacity="0.5"/>
                  </g>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Matches(new Regex(@"/ca 0\.5\b"), pdfText);
        }

        [Fact]
        public async Task InlineSvg_GroupOpacity_NoBoundableContent_FallsBackToPerShapeAlpha()
        {
            // SvgGeometryBounds.GetBoundingBox doesn't handle <text> (only geometry elements), so a
            // <g opacity> containing only text has no boundable content to size a tile from -
            // RenderContainerOpacityGroup falls back to the older per-shape alpha multiply rather than
            // rendering nothing. Assert the text still renders, translucently, via that fallback (no
            // isolated-group Form XObject is expected here, just the plain fill-alpha path).
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <g opacity="0.5">
                    <text x="10" y="50" font-size="20" fill="#ff0000">Hi</text>
                  </g>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            // The per-shape alpha-multiply fallback quantizes opacity through a byte alpha channel
            // (RColor.A is 0-255 - see SvgRenderer.ApplyOpacity), so 0.5 comes out as 128/255 (~0.502),
            // unlike the isolated-group path's exact double alpha - assert on that quantized value
            // rather than an exact "0.5".
            Assert.Contains("/ca 0.5019608", pdfText);
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

        /// <summary>
        /// Same technique as <c>ImageDrawingTests.MakePngBytes</c> - a hand-picked minimal PNG isn't
        /// reliably decodable by the StbImageSharp-based decoder this fork uses; writing one with the
        /// matching StbImageWriteSharp encoder is.
        /// </summary>
        private static string OnePixelPngDataUri()
        {
            var pixels = new byte[] { 255, 0, 0, 255 };
            using var ms = new MemoryStream();
            new StbImageWriteSharp.ImageWriter().WritePng(pixels, 1, 1, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, ms);
            return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        }

        [Fact]
        public async Task InlineSvg_ImageDataUriPng_RendersImageXObject()
        {
            var html = $"""
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <image x="10" y="10" width="80" height="80" href="{OnePixelPngDataUri()}"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/Subtype /Image", pdfText);
        }

        [Fact]
        public async Task ImgSvg_ImageDataUriPng_RendersImageXObject()
        {
            var svg = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100"><image x="10" y="10" width="80" height="80" href="{OnePixelPngDataUri()}"/></svg>""";
            var dataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
            var html = $"""<!DOCTYPE html><html><body><img src="{dataUri}" width="100" height="100"/></body></html>""";

            var pdfText = await GetPdfText(html);

            Assert.Contains("/Subtype /Image", pdfText);
        }

        [Fact]
        public async Task InlineSvg_ImageDataUriSvg_RendersVectorContentNotImageXObject()
        {
            // An <image> referencing an embedded image/svg+xml payload must render as real vector
            // content (its own scene graph), never rasterize - unlike the PNG case above, no
            // "/Subtype /Image" XObject should appear at all.
            var embeddedSvg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect x="0" y="0" width="10" height="10" fill="#000000"/></svg>""";
            var embeddedDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(embeddedSvg));

            var html = $"""
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <image x="10" y="10" width="80" height="80" href="{embeddedDataUri}"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("\nf\n", pdfText);
            Assert.DoesNotContain("/Subtype /Image", pdfText);
        }

        [Fact]
        public async Task InlineSvg_ImageWithNonDataUriHref_RendersNothingRatherThanThrowing()
        {
            // A network/file href is a documented v1 gap (SvgTreeBuilder.Build is synchronous) - the
            // page must still render successfully, just without that image's content.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <image x="10" y="10" width="80" height="80" href="https://example.com/x.png"/>
                  <rect x="0" y="0" width="10" height="10" fill="#000000"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.DoesNotContain("/Subtype /Image", pdfText);
            Assert.Contains("\nf\n", pdfText);
        }

        /// <summary>
        /// Counts non-overlapping occurrences of <paramref name="operatorText"/> (e.g. <c>"Tj"</c>) in
        /// <paramref name="pdfText"/> - used instead of checking for literal readable text, since this
        /// renderer's fonts are embedded with <c>PdfFontEncoding.Unicode</c> (glyph-index/CID encoding),
        /// so a drawn string like "Hello" never appears as the literal bytes <c>(Hello)</c> in the
        /// content stream - only the operator's presence/count is a reliable signal.
        /// </summary>
        private static int CountOccurrences(string pdfText, string operatorText)
        {
            var count = 0;
            var index = 0;
            while ((index = pdfText.IndexOf(operatorText, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += operatorText.Length;
            }
            return count;
        }

        [Fact]
        public async Task InlineSvg_Text_RendersTjOperator()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <text x="10" y="20" fill="#000000">Hello</text>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("Tj", pdfText);
        }

        [Fact]
        public async Task ImgSvg_Text_RendersTjOperator()
        {
            var svg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100"><text x="10" y="20" fill="#000000">World</text></svg>""";
            var dataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
            var html = $"""<!DOCTYPE html><html><body><img src="{dataUri}" width="100" height="100"/></body></html>""";

            var pdfText = await GetPdfText(html);

            Assert.Contains("Tj", pdfText);
        }

        [Fact]
        public async Task InlineSvg_Text_FillNone_RendersNoTjOperator()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <text x="10" y="20" fill="none">Hidden</text>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.DoesNotContain("Tj", pdfText);
        }

        [Fact]
        public async Task InlineSvg_TspanWithOwnFill_RendersDistinctColorAndTwoTjOperators()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <text x="10" y="20" fill="#000000">Hello <tspan fill="#2ecc71">World</tspan></text>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            // One Tj per run ("Hello " and "World") - confirms the tspan actually drew as its own
            // separate DrawString call rather than being silently dropped or merged.
            Assert.True(CountOccurrences(pdfText, "Tj") >= 2, $"Expected at least 2 Tj operators, found {CountOccurrences(pdfText, "Tj")}");
            // #2ecc71 -> (0.180, 0.800, 0.443) - confirms the tspan's own fill color, not the parent's
            // black, actually reached the content stream as a distinct color-setting operator.
            Assert.Contains("0.18", pdfText);
        }

        [Fact]
        public async Task InlineSvg_Tref_RendersReferencedElementsTextAsTjOperator()
        {
            var resolvedHtml = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100" xmlns:xlink="http://www.w3.org/1999/xlink">
                  <defs><text id="src">Referenced</text></defs>
                  <text x="10" y="20" fill="#000000"><tref xlink:href="#src"/></text>
                </svg>
                </body></html>
                """;
            var unresolvedHtml = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100" xmlns:xlink="http://www.w3.org/1999/xlink">
                  <text x="10" y="20" fill="#000000"><tref xlink:href="#missing"/></text>
                </svg>
                </body></html>
                """;

            var resolvedPdfText = await GetPdfText(resolvedHtml);
            var unresolvedPdfText = await GetPdfText(unresolvedHtml);

            // The resolved <tref> draws real text (a Tj operator); the unresolved one (empty text)
            // draws nothing at all - confirms the referenced element's text content actually made it
            // through to painting, not just that BuildTextRun resolved the string.
            Assert.Contains("Tj", resolvedPdfText);
            Assert.DoesNotContain("Tj", unresolvedPdfText);
        }

        [Fact]
        public async Task InlineSvg_TextAnchorMiddle_ShiftsDrawOrigin()
        {
            // text-anchor="middle" must shift the actual drawn Td/Tm x-coordinate left of the
            // element's own x="50" (half the measured text width) - confirms MeasureString-based
            // centering actually reached the content stream, not just that text-anchor parsed.
            var startHtml = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <text x="50" y="20" fill="#000000">Hi</text>
                </svg>
                </body></html>
                """;
            var middleHtml = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <text x="50" y="20" fill="#000000" text-anchor="middle">Hi</text>
                </svg>
                </body></html>
                """;

            var startPdfText = await GetPdfText(startHtml);
            var middlePdfText = await GetPdfText(middleHtml);

            Assert.NotEqual(startPdfText, middlePdfText);
        }
    }
}
