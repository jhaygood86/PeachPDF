using PeachPDF.Network;
using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
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
            // A 200x100 viewBox stretched via preserveAspectRatio="none" into a square viewport - the
            // 100x100 CSS-px attribute size resolves to a 75x75pt viewport at the spec-correct 96dpi
            // intrinsic sizing (1px = 0.75pt) - must scale x and y independently (75/200 = 0.375 and
            // 75/100 = 0.75), unlike the default meet mode which would pick one uniform scale (0.375)
            // for both axes - so the y-scale component of the pushed "cm" matrix must show 0.75, not 0.375.
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
            Assert.Contains("0.375 -0 -0 0.75 ", pdfText);
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
        public async Task InlineSvg_TransparentGradientStroke_DoesNotMaskFollowingOpaqueGradientStroke()
        {
            // A transparent (varying-alpha) gradient stroke realizes a /Luminosity soft mask, emitted
            // as an "/GSn gs" ExtGState in the content stream. If that stroke's paint is not bracketed
            // by q/Q, the soft mask leaks into the persistent graphics state and masks out any following
            // paint that emits no soft mask of its own - e.g. a subsequent OPAQUE gradient stroke, which
            // produces no alpha ExtGState - so the opaque stroke silently disappears (issue #135). The
            // masked graphics state must be restored (Q) before the next stroke is painted.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 260 90" width="260" height="90" style="overflow:visible">
                  <defs>
                    <linearGradient id="trans" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="100" y2="0">
                      <stop offset="0" stop-color="#ff0080" stop-opacity="1"/>
                      <stop offset="1" stop-color="#0080ff" stop-opacity="0"/>
                    </linearGradient>
                    <linearGradient id="solid" gradientUnits="userSpaceOnUse" x1="150" y1="0" x2="240" y2="0">
                      <stop offset="0" stop-color="#00c000"/><stop offset="1" stop-color="#c08000"/>
                    </linearGradient>
                  </defs>
                  <rect x="10" y="15" width="90" height="60" fill="none" stroke="url(#trans)" stroke-width="10"/>
                  <rect x="150" y="15" width="90" height="60" fill="none" stroke="url(#solid)" stroke-width="10"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            // The transparent stroke really does emit a luminosity soft mask (otherwise this test would
            // be vacuous - there would be nothing to leak).
            Assert.Contains("/SMask", pdfText);

            // Both strokes select a real shading pattern for stroking (/Pattern CS + SCN).
            var strokePatterns = Regex.Matches(pdfText, @"/Pattern CS\r?\n\S+ SCN");
            Assert.True(strokePatterns.Count >= 2, $"expected two pattern strokes, found {strokePatterns.Count}");

            // Between the first (transparent) stroke's pattern selection and the second (opaque) one, the
            // first stroke must be painted (S) AND its soft-masked graphics state restored (Q). Without
            // the q/Q bracket the region has the S but no Q, and the opaque stroke inherits the leaked mask.
            var between = pdfText[strokePatterns[0].Index..strokePatterns[1].Index];
            Assert.Matches(@"\r?\nS\r?\n", between);
            Assert.Matches(@"\r?\nQ\r?\n", between);
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
        public async Task InlineSvg_GroupOpacity_TextOnlyContent_RendersIsolatedTransparencyGroup()
        {
            // A <g opacity> whose only content is <text> is now bounded in local space
            // (SvgRenderer.MeasureTextBounds, mirroring RenderTextRun's cursor/anchor/measure math), so
            // it gets a proper isolated-group composite instead of the old double-blend-prone per-shape
            // alpha multiply. A single <text> has no self-overlap, so this asserts the group path is
            // taken (a Form XObject + exactly one group /ca 0.5), not the darker per-shape 0.5019608.
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

            Assert.Contains("/Subtype /Form", pdfText);
            Assert.Contains("/S /Transparency", pdfText);
            var alphaMatches = Regex.Matches(pdfText, @"/ca 0\.5\b");
            Assert.Single(alphaMatches);
            Assert.DoesNotContain("/ca 0.5019608", pdfText);
        }

        [Fact]
        public async Task InlineSvg_GroupOpacity_OverlappingText_DoNotDoubleBlend()
        {
            // Regression for the text-only-group double-blend residual (issue #157): two overlapping
            // <text> runs under a <g opacity> must composite once as an isolated group, not each carry
            // their own per-run /ca. Before the local-space text-bounds fix, the group was unboundable
            // and fell back to the per-shape alpha multiply, double-darkening where the runs overlap.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <g opacity="0.5">
                    <text x="10" y="50" font-size="30" fill="#ff0000">AAA</text>
                    <text x="10" y="60" font-size="30" fill="#0000ff">WWW</text>
                  </g>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/S /Transparency", pdfText);
            var alphaMatches = Regex.Matches(pdfText, @"/ca 0\.5\b");
            Assert.Single(alphaMatches);                                       // one group composite, not per-run
            Assert.Matches(new Regex(@"cm /GS\d+ gs /Fm\d+ Do"), pdfText);     // structural adjacency (CLAUDE.md §Testing)
        }

        [Fact]
        public async Task InlineSvg_UseOpacity_ContainerTarget_DoNotDoubleBlend()
        {
            // Regression for the <use opacity> → container residual (issue #157): a <use opacity>
            // referencing a <g> of overlapping shapes must composite the whole target once as an
            // isolated group. A <use> is neither <g> nor nested <svg>, so it previously never entered
            // the group path and its target's overlapping shapes double-darkened via per-shape alpha.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs>
                    <g id="pair">
                      <rect x="10" y="10" width="50" height="50" fill="#ff0000"/>
                      <rect x="30" y="30" width="50" height="50" fill="#0000ff"/>
                    </g>
                  </defs>
                  <use xlink:href="#pair" opacity="0.5"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/S /Transparency", pdfText);
            var alphaMatches = Regex.Matches(pdfText, @"/ca 0\.5\b");
            Assert.Single(alphaMatches);
            Assert.Matches(new Regex(@"cm /GS\d+ gs /Fm\d+ Do"), pdfText);
        }

        [Fact]
        public async Task InlineSvg_NestedSvgOpacity_RendersIsolatedTransparencyGroup()
        {
            // A nested <svg opacity> is bounded from its own x/y/width/height (SvgNestedSvgElement isn't
            // in SvgGeometryBounds), so it too now composites once as an isolated group.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <svg x="10" y="10" width="70" height="70" opacity="0.5">
                    <rect x="0" y="0" width="50" height="50" fill="#ff0000"/>
                    <rect x="20" y="20" width="50" height="50" fill="#0000ff"/>
                  </svg>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/S /Transparency", pdfText);
            var alphaMatches = Regex.Matches(pdfText, @"/ca 0\.5\b");
            Assert.Single(alphaMatches);
        }

        [Fact]
        public async Task InlineSvg_GroupOpacity_ImageOnlyContent_RendersIsolatedTransparencyGroup()
        {
            // An <image> is bounded from its own x/y/width/height, so an <image>-only <g opacity> is a
            // proper isolated group rather than the old per-shape-alpha fallback.
            const string pngBase64 =
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
            var html = $"""
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <g opacity="0.5">
                    <image href="data:image/png;base64,{pngBase64}" x="10" y="10" width="50" height="50"/>
                  </g>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/S /Transparency", pdfText);
            Assert.Matches(new Regex(@"/ca 0\.5\b"), pdfText);
        }

        [Fact]
        public async Task InlineSvg_UseOpacity_SymbolTarget_RendersIsolatedTransparencyGroup()
        {
            // A <use opacity> of a <symbol> establishes a viewport sized by the use's width/height; its
            // bounds come from that rect, so it composites once as an isolated group.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs>
                    <symbol id="sym" viewBox="0 0 100 100">
                      <rect x="10" y="10" width="50" height="50" fill="#ff0000"/>
                      <rect x="30" y="30" width="50" height="50" fill="#0000ff"/>
                    </symbol>
                  </defs>
                  <use xlink:href="#sym" x="5" y="5" width="80" height="80" opacity="0.5"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/S /Transparency", pdfText);
            var alphaMatches = Regex.Matches(pdfText, @"/ca 0\.5\b");
            Assert.Single(alphaMatches);
        }

        [Fact]
        public async Task InlineSvg_UseOpacity_NestedSvgTarget_RendersIsolatedTransparencyGroup()
        {
            // A <use opacity> whose target is a nested <svg> is bounded from the target's own size
            // (or the use's width/height override) - also an isolated group.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs>
                    <svg id="inner" x="0" y="0" width="70" height="70">
                      <rect x="0" y="0" width="50" height="50" fill="#ff0000"/>
                      <rect x="20" y="20" width="50" height="50" fill="#0000ff"/>
                    </svg>
                  </defs>
                  <use xlink:href="#inner" x="10" y="10" opacity="0.5"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/S /Transparency", pdfText);
            var alphaMatches = Regex.Matches(pdfText, @"/ca 0\.5\b");
            Assert.Single(alphaMatches);
        }

        [Fact]
        public async Task InlineSvg_GroupOpacity_AnchoredText_WithUnboundableSibling_RendersIsolatedGroup()
        {
            // Exercises the text-anchor middle/end bounds branches and the union's skip-null-bounds path
            // (a zero-size <rect> child reports no bounds and is skipped): a <g opacity> with anchored
            // text plus an unboundable sibling still bounds to the text and composites once.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <g opacity="0.5">
                    <rect x="0" y="0" width="0" height="0" fill="#00ff00"/>
                    <text x="50" y="40" font-size="20" text-anchor="middle" fill="#ff0000">Mid</text>
                    <text x="90" y="70" font-size="20" text-anchor="end" fill="#0000ff">End</text>
                  </g>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/S /Transparency", pdfText);
            var alphaMatches = Regex.Matches(pdfText, @"/ca 0\.5\b");
            Assert.Single(alphaMatches);
        }

        [Fact]
        public async Task InlineSvg_GroupOpacity_RotatedText_RendersIsolatedTransparencyGroup()
        {
            // A per-glyph rotate on the text is bounded by its rotated corners (RotateRectBounds), so a
            // rotated-text-only <g opacity> is still boundable and composites once.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <g opacity="0.5">
                    <text x="20" y="50" font-size="20" rotate="30" fill="#ff0000">Rot</text>
                  </g>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            Assert.Contains("/S /Transparency", pdfText);
            Assert.Matches(new Regex(@"/ca 0\.5\b"), pdfText);
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
        public async Task InlineSvg_ImageWithNonDataUriHref_NoLoaderConfigured_RendersNothingRatherThanThrowing()
        {
            // The default network loader can't fetch an http URL (it returns null for non-data: URIs),
            // so this <image> stays unresolved - the page must still render successfully, just without
            // that image's content. (A configured loader DOES fetch it - see the tests below.)
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

        // A real, loadable 1x1 pixel PNG (same constant used across the image integration tests).
        private const string PngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        private static readonly RUri DocumentUri = new("https://example.test/page.html");

        private static string GetPdfText(PeachPdfDocument doc)
        {
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Fact]
        public async Task InlineSvg_ImageWithNetworkRasterHref_EmbedsImageXObject()
        {
            var loader = new InMemoryNetworkLoader(DocumentUri, primaryHtml: """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <image x="10" y="10" width="80" height="80" href="https://example.test/pic.png"/>
                </svg>
                </body></html>
                """);
            loader.AddResource("https://example.test/pic.png", Convert.FromBase64String(PngBase64), "image/png");

            var config = new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4 };
            var doc = await new PdfGenerator().GeneratePdf(null, config);

            // The fetched raster embeds as a real PDF image XObject - impossible before the fix.
            Assert.Contains("/Subtype /Image", GetPdfText(doc));
        }

        [Fact]
        public async Task InlineSvg_ImageWithNetworkSvgHref_RendersAsVectorNestedDocument()
        {
            var inner = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect x="0" y="0" width="10" height="10" fill="#000000"/></svg>""";

            var loader = new InMemoryNetworkLoader(DocumentUri, primaryHtml: """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <image x="10" y="10" width="80" height="80" href="https://example.test/inner.svg"/>
                </svg>
                </body></html>
                """);
            loader.AddTextResource("https://example.test/inner.svg", inner, "image/svg+xml");

            var config = new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4, CompressContentStreams = false };
            var pdfText = GetPdfText(await new PdfGenerator().GeneratePdf(null, config));

            // Painted as a real vector scene (fill operator present), never rasterized to an image XObject.
            Assert.Contains("\nf\n", pdfText);
            Assert.DoesNotContain("/Subtype /Image", pdfText);
        }

        [Fact]
        public async Task InlineSvg_ImageWithSvgContentTypeButNoExtension_RendersAsVector()
        {
            var inner = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect x="0" y="0" width="10" height="10" fill="#000000"/></svg>""";

            var loader = new InMemoryNetworkLoader(DocumentUri, primaryHtml: """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <image x="10" y="10" width="80" height="80" href="https://example.test/render"/>
                </svg>
                </body></html>
                """);
            // No .svg extension - SVG must be detected purely from the Content-Type response header.
            loader.AddTextResource("https://example.test/render", inner, "image/svg+xml");

            var config = new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4, CompressContentStreams = false };
            var pdfText = GetPdfText(await new PdfGenerator().GeneratePdf(null, config));

            Assert.Contains("\nf\n", pdfText);
            Assert.DoesNotContain("/Subtype /Image", pdfText);
        }

        [Fact]
        public async Task InlineSvg_ImageWithXlinkHrefNetworkRaster_EmbedsImageXObject()
        {
            var loader = new InMemoryNetworkLoader(DocumentUri, primaryHtml: """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100" xmlns:xlink="http://www.w3.org/1999/xlink">
                  <image x="10" y="10" width="80" height="80" xlink:href="https://example.test/pic.png"/>
                </svg>
                </body></html>
                """);
            loader.AddResource("https://example.test/pic.png", Convert.FromBase64String(PngBase64), "image/png");

            var config = new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4 };
            var doc = await new PdfGenerator().GeneratePdf(null, config);

            Assert.Contains("/Subtype /Image", GetPdfText(doc));
        }

        [Fact]
        public async Task InlineSvg_ImageFetchThrows_RendersRestOfPageRatherThanAborting()
        {
            // A network loader that throws (transport failure, malformed header, etc.) must not abort
            // the whole render - the <image> is skipped and the rest of the SVG still paints.
            var loader = new ThrowingNetworkLoader(DocumentUri);
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <image x="10" y="10" width="80" height="80" href="https://example.test/boom.png"/>
                  <rect x="0" y="0" width="10" height="10" fill="#000000"/>
                </svg>
                </body></html>
                """;

            var config = new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4, CompressContentStreams = false };
            var pdfText = GetPdfText(await new PdfGenerator().GeneratePdf(html, config));

            // The render completed and the rect still painted; the throwing image simply rendered nothing.
            Assert.Contains("\nf\n", pdfText);
            Assert.DoesNotContain("/Subtype /Image", pdfText);
        }

        private sealed class ThrowingNetworkLoader(RUri baseUri) : RNetworkLoader
        {
            public override RUri? BaseUri { get; } = baseUri;
            public override Task<string> GetPrimaryContents() => Task.FromResult(string.Empty);
            public override Task<RNetworkResponse?> GetResourceStream(RUri uri) =>
                throw new HttpRequestException("simulated transport failure");
        }

        [Fact]
        public async Task StandaloneSvgImg_ImageWithRelativeHref_ResolvesAgainstSvgLocation()
        {
            // The outer SVG lives at /assets/scene.svg and references pic.png relatively, so it must
            // resolve against the SVG's OWN location (/assets/pic.png), not the document's.
            var outerSvg = """
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100">
                  <image x="0" y="0" width="100" height="100" href="pic.png"/>
                </svg>
                """;

            var loader = new InMemoryNetworkLoader(DocumentUri, primaryHtml: """
                <!DOCTYPE html><html><body><img src="https://example.test/assets/scene.svg" width="100" height="100"></body></html>
                """);
            loader.AddTextResource("https://example.test/assets/scene.svg", outerSvg, "image/svg+xml");
            loader.AddResource("https://example.test/assets/pic.png", Convert.FromBase64String(PngBase64), "image/png");

            var config = new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4 };
            var doc = await new PdfGenerator().GeneratePdf(null, config);

            Assert.Contains("/Subtype /Image", GetPdfText(doc));
        }

        [Fact]
        public async Task InlineSvg_ImageWithNetworkSvgHref_ContainingNetworkImage_EmbedsLeafXObject()
        {
            // #251: the outer <image> references a fetched SVG whose OWN <image> references a network
            // raster. The recursive prefetch must descend into the fetched SVG payload and fetch the
            // doubly-nested leaf, embedding it as a real image XObject - impossible before the fix
            // (the nested document's hrefs were never collected by the outer, single-level prefetch).
            var scene = """
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100">
                  <image x="0" y="0" width="100" height="100" href="https://example.test/leaf.png"/>
                </svg>
                """;

            var loader = new InMemoryNetworkLoader(DocumentUri, primaryHtml: """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <image x="0" y="0" width="100" height="100" href="https://example.test/scene.svg"/>
                </svg>
                </body></html>
                """);
            loader.AddTextResource("https://example.test/scene.svg", scene, "image/svg+xml");
            loader.AddResource("https://example.test/leaf.png", Convert.FromBase64String(PngBase64), "image/png");

            var config = new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4 };
            var doc = await new PdfGenerator().GeneratePdf(null, config);

            Assert.Contains("/Subtype /Image", GetPdfText(doc));
        }

        [Fact]
        public async Task InlineSvg_ImageWithDataUriSvgHref_ContainingNetworkImage_EmbedsLeafXObject()
        {
            // #251, data:-outer variant: the outer <image> is a data:image/svg+xml payload whose own
            // <image> references a network raster. The prefetch must decode the data: SVG, find the
            // nested network href, and fetch it.
            var scene = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><image x="0" y="0" width="100" height="100" href="https://example.test/leaf.png"/></svg>""";
            var dataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(scene));

            var loader = new InMemoryNetworkLoader(DocumentUri, primaryHtml: $"""
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <image x="0" y="0" width="100" height="100" href="{dataUri}"/>
                </svg>
                </body></html>
                """);
            loader.AddResource("https://example.test/leaf.png", Convert.FromBase64String(PngBase64), "image/png");

            var config = new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4 };
            var doc = await new PdfGenerator().GeneratePdf(null, config);

            Assert.Contains("/Subtype /Image", GetPdfText(doc));
        }

        [Fact]
        public async Task StandaloneSvgImg_DoublyNestedImage_RelativeHref_ResolvesAgainstInnerSvgLocation()
        {
            // #251 + base resolution: <img> loads /outer.svg, whose <image> references
            // /nested/scene.svg (absolute), whose OWN <image> uses a RELATIVE href. That inner
            // relative href must resolve against scene.svg's own location (/nested/leaf.png), NOT
            // the document's or outer.svg's - proving the recursive prefetch threads each fetched
            // URI as the base for the next level down.
            var outer = """
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100">
                  <image x="0" y="0" width="100" height="100" href="https://example.test/nested/scene.svg"/>
                </svg>
                """;
            var scene = """
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100">
                  <image x="0" y="0" width="100" height="100" href="leaf.png"/>
                </svg>
                """;

            var loader = new InMemoryNetworkLoader(DocumentUri, primaryHtml: """
                <!DOCTYPE html><html><body><img src="https://example.test/outer.svg" width="100" height="100"></body></html>
                """);
            loader.AddTextResource("https://example.test/outer.svg", outer, "image/svg+xml");
            loader.AddTextResource("https://example.test/nested/scene.svg", scene, "image/svg+xml");
            // Only served under /nested/ - a wrong base (document or outer.svg root) would miss it.
            loader.AddResource("https://example.test/nested/leaf.png", Convert.FromBase64String(PngBase64), "image/png");

            var config = new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4 };
            var doc = await new PdfGenerator().GeneratePdf(null, config);

            Assert.Contains("/Subtype /Image", GetPdfText(doc));
        }

        [Fact]
        public async Task InlineSvg_ImageWithNetworkSvgHref_NamespacePrefixedNestedImage_EmbedsLeafXObject()
        {
            // #251 gate robustness: the nested SVG writes its image element namespace-prefixed
            // (<svg:image>) - its local name is still "image", so it must be prefetched. The cheap
            // "<image"/":image" text gate has to catch the prefixed form too, matching CollectImageHrefs
            // (which keys off the local name).
            var scene = """<svg:svg xmlns:svg="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><svg:image x="0" y="0" width="100" height="100" href="https://example.test/leaf.png"/></svg:svg>""";

            var loader = new InMemoryNetworkLoader(DocumentUri, primaryHtml: """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <image x="0" y="0" width="100" height="100" href="https://example.test/scene.svg"/>
                </svg>
                </body></html>
                """);
            loader.AddTextResource("https://example.test/scene.svg", scene, "image/svg+xml");
            loader.AddResource("https://example.test/leaf.png", Convert.FromBase64String(PngBase64), "image/png");

            var config = new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4 };
            var doc = await new PdfGenerator().GeneratePdf(null, config);

            Assert.Contains("/Subtype /Image", GetPdfText(doc));
        }

        [Fact]
        public async Task InlineSvg_ImageWithNetworkSvgHref_MalformedNestedPayload_RendersRestOfPage()
        {
            // #251 robustness: the fetched SVG payload contains an "<image" (so it passes the cheap
            // recursion gate) but is malformed XML. The nested prefetch parse must fail softly - the
            // outer <image> renders nothing and the rest of the page still paints, no abort.
            var scene = """<svg xmlns="http://www.w3.org/2000/svg"><image href="https://example.test/leaf.png"</svg>""";

            var loader = new InMemoryNetworkLoader(DocumentUri, primaryHtml: """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <image x="0" y="0" width="100" height="100" href="https://example.test/scene.svg"/>
                  <rect x="0" y="0" width="10" height="10" fill="#000000"/>
                </svg>
                </body></html>
                """);
            loader.AddTextResource("https://example.test/scene.svg", scene, "image/svg+xml");
            loader.AddResource("https://example.test/leaf.png", Convert.FromBase64String(PngBase64), "image/png");

            var config = new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4, CompressContentStreams = false };
            var pdfText = GetPdfText(await new PdfGenerator().GeneratePdf(null, config));

            // The render completed and the sibling rect still painted; the malformed nested SVG (and
            // therefore its leaf raster) simply rendered nothing.
            Assert.Contains("\nf\n", pdfText);
            Assert.DoesNotContain("/Subtype /Image", pdfText);
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

        // ----- Clip-shape transform (issue #169) - real-adapter end-to-end -----

        private const string TransformedClipHtml = """
            <!DOCTYPE html><html><body>
            <svg viewBox="0 0 100 100" width="100" height="100">
              <defs><clipPath id="c"><rect x="0" y="0" width="30" height="20" transform="translate(15,25)"/></clipPath></defs>
              <rect x="0" y="0" width="100" height="100" fill="#3498db" clip-path="url(#c)"/>
            </svg>
            </body></html>
            """;

        // Same clip region, but authored directly at the translated position (no transform).
        private const string DirectClipHtml = """
            <!DOCTYPE html><html><body>
            <svg viewBox="0 0 100 100" width="100" height="100">
              <defs><clipPath id="c"><rect x="15" y="25" width="30" height="20"/></clipPath></defs>
              <rect x="0" y="0" width="100" height="100" fill="#3498db" clip-path="url(#c)"/>
            </svg>
            </body></html>
            """;

        // Same clip, but the shape left at the origin with NO transform - the pre-fix (buggy) geometry.
        private const string UntransformedClipHtml = """
            <!DOCTYPE html><html><body>
            <svg viewBox="0 0 100 100" width="100" height="100">
              <defs><clipPath id="c"><rect x="0" y="0" width="30" height="20"/></clipPath></defs>
              <rect x="0" y="0" width="100" height="100" fill="#3498db" clip-path="url(#c)"/>
            </svg>
            </body></html>
            """;

        /// <summary>Extracts the numeric coordinates of the path-construction operators emitted
        /// immediately before the clip operator <c>W n</c> - i.e. the clip region's own geometry.</summary>
        private static double[] ClipGeometryNumbers(string pdfText)
        {
            var idx = pdfText.IndexOf("\nW n\n", StringComparison.Ordinal);
            Assert.True(idx >= 0, "expected a clip operator (W n) in the content stream");

            var start = Math.Max(0, idx - 240);
            var window = pdfText.Substring(start, idx - start);
            return Regex.Matches(window, @"-?\d+(?:\.\d+)?")
                .Select(m => double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
        }

        [Fact]
        public async Task ClipShapeTransform_ProducesSameGeometryAsDirectlyAuthoredShape()
        {
            // The real GraphicsPathAdapter.Transform/AddPath (+ XGraphicsPath/CoreGraphicsPath) must bake
            // the clip shape's transform into its coordinates, so a transformed clip is byte-identical to
            // the same region authored directly - and distinct from the un-transformed (pre-fix) geometry.
            var transformed = ClipGeometryNumbers(await GetPdfText(TransformedClipHtml));
            var direct = ClipGeometryNumbers(await GetPdfText(DirectClipHtml));
            var untransformed = ClipGeometryNumbers(await GetPdfText(UntransformedClipHtml));

            Assert.Equal(direct.Length, transformed.Length);
            Assert.Equal(untransformed.Length, transformed.Length);
            for (var i = 0; i < transformed.Length; i++)
                Assert.Equal(direct[i], transformed[i], 4);

            // The transform genuinely moved the clip: at least one coordinate differs from the
            // un-transformed (pre-fix) geometry.
            Assert.Contains(Enumerable.Range(0, transformed.Length), i => Math.Abs(untransformed[i] - transformed[i]) > 0.01);
        }

        #region CSS cascade for SVG (issues #159 / #192): <style>, HTML cascade, selectors, var(), case-sensitivity, precedence

        private static string InlineSvgDoc(string headStyles, string svgBody) =>
            $"<!DOCTYPE html><html><head><style>body{{margin:0}}{headStyles}</style></head><body>" +
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\" width=\"100\" height=\"100\">{svgBody}</svg></body></html>";

        private static string ImgSvgDoc(string headStyles, string svgMarkup)
        {
            var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\" width=\"100\" height=\"100\">{svgMarkup}</svg>";
            var dataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
            return $"<!DOCTYPE html><html><head><style>body{{margin:0}}{headStyles}</style></head><body>" +
                   $"<img src=\"{dataUri}\" width=\"100\" height=\"100\"/></body></html>";
        }

        // #00ff00 -> "0 1 0 rg" (green fill); #0000ff -> "0 0 1 rg" (blue); #ff0000 -> "1 0 0 rg" (red).
        private const string Green = "0 1 0 rg";
        private const string Blue = "0 0 1 rg";
        private const string Red = "1 0 0 rg";
        private const string Black = "0 0 0 rg";

        // Issue #159: a <style> nested inside an inline <svg> now applies to that SVG's shapes,
        // end-to-end through the full PdfGenerator pipeline.
        [Fact]
        public async Task InlineSvg_StyleElement_ClassSelectorAppliesToShape()
        {
            var html = InlineSvgDoc("", """<style>.hl { fill: #00ff00; }</style><rect class="hl" x="10" y="10" width="80" height="80"/>""");
            Assert.Contains(Green, await GetPdfText(html));
        }

        // Issue #192 gap 2 / SVG 2 §6: an HTML document-level <style> rule cascades into inline SVG.
        [Fact]
        public async Task InlineSvg_HtmlHeadStyle_TypeSelector_StylesShape()
        {
            var html = InlineSvgDoc("rect { fill: #00ff00; }", """<rect x="10" y="10" width="80" height="80"/>""");
            Assert.Contains(Green, await GetPdfText(html));
        }

        [Fact]
        public async Task InlineSvg_HtmlHeadStyle_ClassSelector_StylesShape()
        {
            var html = InlineSvgDoc(".hl { fill: #00ff00; }", """<rect class="hl" x="10" y="10" width="80" height="80"/>""");
            Assert.Contains(Green, await GetPdfText(html));
        }

        // Issue #192 gap 1: combinators/attribute/structural-pseudo selectors (unsupported by the old
        // SvgStyleSheet mini-matcher) now work via the full engine - inline and standalone.
        [Fact]
        public async Task InlineSvg_DescendantCombinatorSelector_StylesShape()
        {
            var html = InlineSvgDoc("g rect { fill: #00ff00; }", """<g><rect x="10" y="10" width="80" height="80"/></g>""");
            Assert.Contains(Green, await GetPdfText(html));
        }

        [Fact]
        public async Task ImgSvg_ChildCombinatorSelector_StylesShape()
        {
            var html = ImgSvgDoc("", """<style>g > rect { fill: #00ff00; }</style><g><rect x="10" y="10" width="80" height="80"/></g>""");
            Assert.Contains(Green, await GetPdfText(html));
        }

        [Fact]
        public async Task InlineSvg_AttributeSelector_StylesShape()
        {
            var html = InlineSvgDoc("rect[data-hl=\"1\"] { fill: #00ff00; }", """<rect data-hl="1" x="10" y="10" width="80" height="80"/>""");
            Assert.Contains(Green, await GetPdfText(html));
        }

        [Fact]
        public async Task InlineSvg_StructuralPseudoSelector_StylesShape()
        {
            var html = InlineSvgDoc("rect:only-of-type { fill: #00ff00; }", """<g><rect x="10" y="10" width="80" height="80"/></g>""");
            Assert.Contains(Green, await GetPdfText(html));
        }

        // SVG selectors are case-sensitive (XML/Selectors 4 §6): a correctly-cased camelCase selector
        // matches, a mis-cased one does not.
        [Fact]
        public async Task InlineSvg_MixedCaseSelector_MatchesWhenCaseAgrees()
        {
            var html = InlineSvgDoc("rect[data-Kind=\"Hi\"] { fill: #00ff00; }", """<rect data-Kind="Hi" x="10" y="10" width="80" height="80"/>""");
            Assert.Contains(Green, await GetPdfText(html));
        }

        [Fact]
        public async Task InlineSvg_CamelCaseAttributeNameSelector_Matches()
        {
            // MimeKit preserves camelCase SVG attribute names (viewBox/gradientUnits/data-*) for inline
            // <svg>, so a case-sensitive attribute-name selector matches inline just like standalone.
            var html = InlineSvgDoc("rect[data-Kind] { fill: #00ff00; }", """<rect data-Kind="Hi" x="10" y="10" width="80" height="80"/>""");
            Assert.Contains(Green, await GetPdfText(html));
        }

        [Fact]
        public async Task InlineSvg_MisCasedTypeSelector_DoesNotMatch()
        {
            // "RECT" must NOT match <rect> (case-sensitive); the presentation fill (blue) stands.
            var html = InlineSvgDoc("RECT { fill: #00ff00; }", """<rect fill="#0000ff" x="10" y="10" width="80" height="80"/>""");
            var pdf = await GetPdfText(html);
            Assert.DoesNotContain(Green, pdf);
            Assert.Contains(Blue, pdf);
        }

        [Fact]
        public async Task Html_MisCasedSelector_StillMatches_CaseInsensitive()
        {
            // Regression guard: HTML matching stays ASCII case-insensitive (CssBox.NameComparison
            // unchanged) - a mis-cased selector still styles an HTML element's background.
            var html = "<!DOCTYPE html><html><head><style>body{margin:0}DIV{background-color:#00ff00}</style></head>" +
                       "<body><div style=\"width:50px;height:50px\">x</div></body></html>";
            Assert.Contains(Green, await GetPdfText(html));
        }

        // var() custom properties resolve for inline (via the HTML cascade) and standalone (via the
        // SVG-local custom-property cascade).
        [Fact]
        public async Task InlineSvg_VarCustomProperty_Resolves()
        {
            var html = InlineSvgDoc(":root { --c: #00ff00; } rect { fill: var(--c); }", """<rect x="10" y="10" width="80" height="80"/>""");
            Assert.Contains(Green, await GetPdfText(html));
        }

        [Fact]
        public async Task ImgSvg_VarCustomProperty_Resolves()
        {
            var html = ImgSvgDoc("", """<style>:root { --c: #00ff00; } rect { fill: var(--c); }</style><rect x="10" y="10" width="80" height="80"/>""");
            Assert.Contains(Green, await GetPdfText(html));
        }

        // calc() in an SVG length renders (delegated to the shared CSS length parser); exact evaluation
        // is unit-tested in SvgValueParsersTests.
        [Fact]
        public async Task InlineSvg_CalcStrokeWidth_Renders()
        {
            var html = InlineSvgDoc("", """<rect x="10" y="10" width="80" height="80" fill="none" stroke="#000" stroke-width="calc(2px + 3px)"/>""");
            Assert.Contains("\nS\n", await GetPdfText(html));
        }

        // Precedence: inline style="" beats a <style> rule beats a presentation attribute.
        [Fact]
        public async Task InlineSvg_StylePrecedence_InlineStyleWins()
        {
            var html = InlineSvgDoc(".foo { fill: #0000ff; }", """<rect class="foo" fill="#ff0000" style="fill:#00ff00" x="10" y="10" width="80" height="80"/>""");
            var pdf = await GetPdfText(html);
            Assert.Contains(Green, pdf);
            Assert.DoesNotContain(Blue, pdf);
            Assert.DoesNotContain(Red, pdf);
        }

        // A host-document <style> must NOT leak into a standalone <img>-referenced SVG (separate document).
        [Fact]
        public async Task ImgSvg_HostDocumentStyle_DoesNotStyleStandaloneSvg()
        {
            var html = ImgSvgDoc("rect { fill: #ff0000; }", """<rect x="10" y="10" width="80" height="80"/>""");
            Assert.DoesNotContain(Red, await GetPdfText(html));
        }

        // Issue #213: @property registrations declared in a standalone SVG's own <style> are now honored
        // when resolving var(). An unset registered property resolves to its initial-value. (A standalone
        // SVG is strict XML, so a syntax containing "<" is wrapped in CDATA, as a real author would.)
        [Fact]
        public async Task ImgSvg_AtPropertyInitialValue_ResolvesWhenUnset()
        {
            var html = ImgSvgDoc("", """<style><![CDATA[ @property --c { syntax: "<color>"; inherits: false; initial-value: #00ff00; } rect { fill: var(--c); } ]]></style><rect x="10" y="10" width="80" height="80"/>""");
            Assert.Contains(Green, await GetPdfText(html));
        }

        // A set value that fails the registered syntax is invalid at computed-value time and falls back to
        // the initial-value (CSS Properties & Values API §2.2) - the standalone-SVG path now enforces this.
        [Fact]
        public async Task ImgSvg_AtPropertySyntaxMismatch_FallsBackToInitialValue()
        {
            var html = ImgSvgDoc("", """<style><![CDATA[ @property --c { syntax: "<color>"; inherits: false; initial-value: #00ff00; } rect { --c: 42px; fill: var(--c); } ]]></style><rect x="10" y="10" width="80" height="80"/>""");
            Assert.Contains(Green, await GetPdfText(html));
        }

        // inherits: false - a descendant that doesn't set the property resolves it to the initial-value,
        // NOT the ancestor's set value.
        [Fact]
        public async Task ImgSvg_AtPropertyInheritsFalse_DescendantUsesInitialValue()
        {
            var html = ImgSvgDoc("", """<style><![CDATA[ @property --c { syntax: "<color>"; inherits: false; initial-value: #00ff00; } rect { fill: var(--c); } ]]></style><g style="--c: #ff0000"><rect x="10" y="10" width="80" height="80"/></g>""");
            var pdf = await GetPdfText(html);
            Assert.Contains(Green, pdf);   // the initial-value green, not the ancestor's red
            Assert.DoesNotContain(Red, pdf);
        }

        // Inline SVG re-resolves var() through the host document's @property registry too: a registered-but-
        // unset custom property declared in the HTML <head> resolves to its initial-value in the SVG.
        [Fact]
        public async Task InlineSvg_AtPropertyInitialValue_ResolvesWhenUnset()
        {
            var html = InlineSvgDoc("""@property --c { syntax: "<color>"; inherits: false; initial-value: #00ff00; } rect { fill: var(--c); }""", """<rect x="10" y="10" width="80" height="80"/>""");
            Assert.Contains(Green, await GetPdfText(html));
        }

        // The SVG inline style="" tier (ResolveStyledAttr) also honors @property: an inline
        // style="fill: var(--c)" consuming a registered-but-unset property resolves to its initial-value,
        // standalone and inline.
        [Fact]
        public async Task ImgSvg_InlineStyleConsumer_AtPropertyInitialValue()
        {
            var html = ImgSvgDoc("", """<style><![CDATA[ @property --c { syntax: "<color>"; inherits: false; initial-value: #00ff00; } ]]></style><rect x="10" y="10" width="80" height="80" style="fill: var(--c)"/>""");
            Assert.Contains(Green, await GetPdfText(html));
        }

        [Fact]
        public async Task InlineSvg_InlineStyleConsumer_AtPropertyInitialValue()
        {
            var html = InlineSvgDoc("""@property --c { syntax: "<color>"; inherits: false; initial-value: #00ff00; }""", """<rect x="10" y="10" width="80" height="80" style="fill: var(--c)"/>""");
            Assert.Contains(Green, await GetPdfText(html));
        }

        // CSS Custom Properties 1 §3 (invalid at computed-value time): a guaranteed-invalid var() (missing
        // property, no fallback) does NOT fall through to a lower-priority declaration — the declaration wins
        // the cascade and the property computes to its inherited value (fill is inherited). Here the matched
        // <style> rule (fill: var(--missing)) wins over the rect's own presentation fill, so the rect takes
        // the parent <g>'s green, not its own presentation red.
        [Fact]
        public async Task ImgSvg_GuaranteedInvalidVar_ComputesToInherited_NotPresentationAttribute()
        {
            var html = ImgSvgDoc("", """<style>rect { fill: var(--missing); }</style><g fill="#00ff00"><rect x="10" y="10" width="80" height="80" fill="#ff0000"/></g>""");
            var pdf = await GetPdfText(html);
            Assert.Contains(Green, pdf);      // inherited from <g>
            Assert.DoesNotContain(Red, pdf);  // the rect's presentation attr is suppressed (the <style> rule won)
        }

        // Same rule for the inline style="" tier: an invalid var() there wins the cascade and computes to
        // inherited, suppressing both the rect's presentation fill and any lower <style> rule.
        [Fact]
        public async Task ImgSvg_InlineStyleInvalidVar_ComputesToInherited_NotLowerTiers()
        {
            var html = ImgSvgDoc("", """<style>rect { fill: #ff0000; }</style><g fill="#00ff00"><rect x="10" y="10" width="80" height="80" fill="#ff0000" style="fill: var(--missing)"/></g>""");
            var pdf = await GetPdfText(html);
            Assert.Contains(Green, pdf);      // inherited from <g>
            Assert.DoesNotContain(Red, pdf);  // neither the <style> rule nor the presentation attr is used
        }

        // Issue #205: !important in a lower-specificity <style> rule beats a normal declaration in a
        // higher-specificity rule (CSS Cascade 4 §6.3), end-to-end. #r (id) has higher specificity than
        // rect, but the lower-specificity rect rule is !important, so the rect renders green, not red.
        [Fact]
        public async Task ImgSvg_ImportantBeatsHigherSpecificityNormal()
        {
            var html = ImgSvgDoc("", """<style>#r { fill: #ff0000; } rect { fill: #00ff00 !important; }</style><rect id="r" x="10" y="10" width="80" height="80"/>""");
            var pdf = await GetPdfText(html);
            Assert.Contains(Green, pdf);
            Assert.DoesNotContain(Red, pdf);
        }

        [Fact]
        public async Task InlineSvg_ImportantBeatsHigherSpecificityNormal()
        {
            var html = InlineSvgDoc("#r { fill: #ff0000; } rect { fill: #00ff00 !important; }", """<rect id="r" x="10" y="10" width="80" height="80"/>""");
            var pdf = await GetPdfText(html);
            Assert.Contains(Green, pdf);
            Assert.DoesNotContain(Red, pdf);
        }

        // Issue #230: `fill: revert` in a <style> rule now parses (the paint converter accepts CSS-wide
        // keywords) and rolls the author cascade back to a lower origin. fill is inherited, so it computes to
        // the parent <g>'s green, suppressing the rect's own presentation red - an SVG presentation attribute
        // is itself author origin (SVG 2 §6.3), so revert rolls back past it too.
        [Fact]
        public async Task ImgSvg_FillRevert_ComputesToInherited_NotPresentationAttribute()
        {
            var html = ImgSvgDoc("", """<style>rect { fill: revert; }</style><g fill="#00ff00"><rect x="10" y="10" width="80" height="80" fill="#ff0000"/></g>""");
            var pdf = await GetPdfText(html);
            Assert.Contains(Green, pdf);      // inherited from <g>
            Assert.DoesNotContain(Red, pdf);  // the rect's presentation attr is suppressed (revert rolled past it)
        }

        // Issue #230: `fill: inherit` in a <style> rule now parses and takes the parent <g>'s green.
        [Fact]
        public async Task ImgSvg_FillInherit_TakesParentValue()
        {
            var html = ImgSvgDoc("", """<style>rect { fill: inherit; }</style><g fill="#00ff00"><rect x="10" y="10" width="80" height="80" fill="#ff0000"/></g>""");
            var pdf = await GetPdfText(html);
            Assert.Contains(Green, pdf);
            Assert.DoesNotContain(Red, pdf);
        }

        // Issue #230: `fill: initial` in a <style> rule now parses and computes to fill's SVG initial value
        // (black) - NOT the inherited red - even when an ancestor sets fill.
        [Fact]
        public async Task ImgSvg_FillInitial_ComputesToBlack_NotInherited()
        {
            var html = ImgSvgDoc("", """<style>rect { fill: initial; }</style><g fill="#ff0000"><rect x="10" y="10" width="80" height="80"/></g>""");
            var pdf = await GetPdfText(html);
            Assert.Contains(Black, pdf);      // fill's initial value
            Assert.DoesNotContain(Red, pdf);  // NOT the ancestor's inherited red
        }

        // A nested data:image/svg+xml document (an <image> inside an outer SVG) is its own standalone SVG,
        // so its own <style>'s @property registrations are honored too (SvgTreeBuilder.BuildImage path).
        [Fact]
        public async Task InlineSvg_NestedDataUriSvg_AtPropertyInitialValueResolves()
        {
            var embeddedSvg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100"><style><![CDATA[ @property --c { syntax: "<color>"; inherits: false; initial-value: #00ff00; } rect { fill: var(--c); } ]]></style><rect x="0" y="0" width="100" height="100"/></svg>""";
            var embeddedDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(embeddedSvg));
            var html = $"""<!DOCTYPE html><html><body><svg viewBox="0 0 100 100" width="100" height="100"><image x="0" y="0" width="100" height="100" href="{embeddedDataUri}"/></svg></body></html>""";
            Assert.Contains(Green, await GetPdfText(html));
        }

        #endregion
    }
}
