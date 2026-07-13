using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Xml.Linq;

namespace PeachPDF.Tests.Svg
{
    public class SvgTreeBuilderTests
    {
        private static readonly PdfSharpAdapter Adapter = new();

        private static SvgDocument BuildFrom(string markup) =>
            SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);

        [Fact]
        public void Build_FromCssBoxTree_ProducesExpectedStructure()
        {
            var root = HtmlParser.ParseDocument(SvgTestFixture.Markup);
            var svgBox = DomUtils.GetBoxByTagName(root, "svg");

            Assert.NotNull(svgBox);

            var document = SvgTreeBuilder.Build(new CssBoxSvgSourceNode(svgBox!), Adapter);

            AssertExpectedStructure(document);
        }

        [Fact]
        public void Build_FromXmlDocument_ProducesEquivalentStructure()
        {
            var xdoc = XDocument.Parse(SvgTestFixture.Markup);
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(xdoc.Root!), Adapter);

            AssertExpectedStructure(document);
        }

        private static void AssertExpectedStructure(SvgDocument document)
        {
            Assert.NotNull(document.ViewBox);
            Assert.Equal(200, document.ViewBox!.Value.Width, 3);
            Assert.Equal(200, document.ViewBox!.Value.Height, 3);
            Assert.Equal(200, document.Width);
            Assert.Equal(200, document.Height);

            Assert.Equal(2, document.Gradients.Count);
            Assert.True(document.Gradients.ContainsKey("bgGradient"));
            Assert.True(document.Gradients.ContainsKey("shineGradient"));

            var bgGradient = Assert.IsType<SvgLinearGradient>(document.Gradients["bgGradient"]);
            Assert.Equal(2, bgGradient.Stops.Count);
            Assert.True(bgGradient.GradientUnitsUserSpaceOnUse);

            var shineGradient = Assert.IsType<SvgRadialGradient>(document.Gradients["shineGradient"]);
            Assert.NotNull(shineGradient.GradientTransform);

            Assert.Single(document.ClipPaths);
            var clipPath = document.ClipPaths["circleClip"];
            Assert.Single(clipPath.Shapes);
            var clippedUse = Assert.IsType<SvgUseElement>(clipPath.Shapes[0]);
            Assert.IsType<SvgCircleElement>(clippedUse.Target);

            // <defs> is skipped; only the two <g> elements and the ribbon <polygon> remain.
            Assert.Equal(3, document.Children.Count);

            var clippedGroup = Assert.IsType<SvgGroupElement>(document.Children[0]);
            Assert.Equal("circleClip", clippedGroup.ClipPathRef);
            Assert.Equal(2, clippedGroup.Children.Count);
            Assert.IsType<SvgPolygonElement>(clippedGroup.Children[0]);
            var opacityGroup = Assert.IsType<SvgGroupElement>(clippedGroup.Children[1]);
            Assert.Equal(0.5, opacityGroup.Opacity, 3);
            Assert.IsType<SvgCircleElement>(Assert.Single(opacityGroup.Children));

            var checkmarkGroup = Assert.IsType<SvgGroupElement>(document.Children[1]);
            Assert.NotNull(checkmarkGroup.Transform);
            var path = Assert.IsType<SvgPathElement>(Assert.Single(checkmarkGroup.Children));
            Assert.True(path.Segments.Count > 0);
            Assert.Contains(path.Segments, s => s.Kind == PathSegmentKind.ArcTo);
            Assert.Contains(path.Segments, s => s.Kind == PathSegmentKind.CubicBezierTo);

            var ribbon = Assert.IsType<SvgPolygonElement>(document.Children[2]);
            Assert.Equal(6, ribbon.Points.Length);
        }

        [Fact]
        public void FillRule_EvenOdd_IsAppliedToElement()
        {
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><path id="star" fill-rule="evenodd" d="M0,0 L10,0 L10,10 Z"/></svg>""");

            var path = Assert.IsType<SvgPathElement>(Assert.Single(document.Children));
            Assert.Equal(RFillMode.EvenOdd, path.FillRule);
        }

        [Fact]
        public void FillRule_DefaultsToNonzero()
        {
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><path d="M0,0 L10,0 L10,10 Z"/></svg>""");

            var path = Assert.IsType<SvgPathElement>(Assert.Single(document.Children));
            Assert.Equal(RFillMode.Nonzero, path.FillRule);
        }

        [Fact]
        public void FillRule_IsInheritedFromGroup()
        {
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><g fill-rule="evenodd"><path d="M0,0 L10,0 L10,10 Z"/></g></svg>""");

            var group = Assert.IsType<SvgGroupElement>(Assert.Single(document.Children));
            var path = Assert.IsType<SvgPathElement>(Assert.Single(group.Children));
            Assert.Equal(RFillMode.EvenOdd, path.FillRule);
        }

        [Fact]
        public void FillOpacityAndStrokeOpacity_AreDistinctFromOpacity()
        {
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><circle cx="5" cy="5" r="5" opacity="0.8" fill-opacity="0.4" stroke-opacity="0.2"/></svg>""");

            var circle = Assert.IsType<SvgCircleElement>(Assert.Single(document.Children));
            Assert.Equal(0.8, circle.Opacity, 3);
            Assert.Equal(0.4, circle.FillOpacity, 3);
            Assert.Equal(0.2, circle.StrokeOpacity, 3);
        }

        [Fact]
        public void FillOpacity_IsInheritedFromGroup()
        {
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><g fill-opacity="0.3"><circle cx="5" cy="5" r="5"/></g></svg>""");

            var group = Assert.IsType<SvgGroupElement>(Assert.Single(document.Children));
            var circle = Assert.IsType<SvgCircleElement>(Assert.Single(group.Children));
            Assert.Equal(0.3, circle.FillOpacity, 3);
        }

        [Fact]
        public void ClipPath_ClipRule_DefaultsToNonzero()
        {
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <defs><clipPath id="clip"><circle cx="5" cy="5" r="5"/></clipPath></defs>
                  <circle clip-path="url(#clip)" cx="5" cy="5" r="5"/>
                </svg>
                """);

            Assert.Equal(RFillMode.Nonzero, document.ClipPaths["clip"].ClipRule);
        }

        [Fact]
        public void ClipPath_ClipRule_EvenOdd_IsApplied()
        {
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <defs><clipPath id="clip" clip-rule="evenodd"><circle cx="5" cy="5" r="5"/></clipPath></defs>
                  <circle clip-path="url(#clip)" cx="5" cy="5" r="5"/>
                </svg>
                """);

            Assert.Equal(RFillMode.EvenOdd, document.ClipPaths["clip"].ClipRule);
        }

        [Fact]
        public void CircleAttributes_Percentage_ResolveAgainstViewBox()
        {
            // viewBox is 0 0 200 100 -> cx resolves against width (200), cy against height (100),
            // r against the SVG-spec diagonal formula sqrt((200^2+100^2)/2).
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100"><circle cx="50%" cy="50%" r="10%"/></svg>""");

            var circle = Assert.IsType<SvgCircleElement>(Assert.Single(document.Children));
            Assert.Equal(100, circle.Cx, 3);
            Assert.Equal(50, circle.Cy, 3);
            Assert.Equal(15.811, circle.R, 3);
        }

        [Fact]
        public void StrokeWidth_UnitSuffix_ConvertsToPixels()
        {
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><path d="M0,0 L10,0" stroke="black" stroke-width="1pt"/></svg>""");

            var path = Assert.IsType<SvgPathElement>(Assert.Single(document.Children));
            Assert.Equal(96.0 / 72.0, path.StrokeWidth, 3);
        }

        [Fact]
        public void StrokeLineCapAndLineJoin_AreParsed()
        {
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><path d="M0,0 L10,0" stroke="black" stroke-linecap="round" stroke-linejoin="bevel"/></svg>""");

            var path = Assert.IsType<SvgPathElement>(Assert.Single(document.Children));
            Assert.Equal(RLineCap.Round, path.StrokeLineCap);
            Assert.Equal(RLineJoin.Bevel, path.StrokeLineJoin);
        }

        [Fact]
        public void StrokeDashArrayAndOffset_AreParsed()
        {
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><path d="M0,0 L10,0" stroke="black" stroke-dasharray="4,2" stroke-dashoffset="1"/></svg>""");

            var path = Assert.IsType<SvgPathElement>(Assert.Single(document.Children));
            Assert.Equal([4, 2], path.StrokeDashArray);
            Assert.Equal(1, path.StrokeDashOffset, 3);
        }

        [Fact]
        public void StrokeDashArray_IsInheritedFromGroup()
        {
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><g stroke-dasharray="4,2"><path d="M0,0 L10,0" stroke="black"/></g></svg>""");

            var group = Assert.IsType<SvgGroupElement>(Assert.Single(document.Children));
            var path = Assert.IsType<SvgPathElement>(Assert.Single(group.Children));
            Assert.Equal([4, 2], path.StrokeDashArray);
        }

        [Fact]
        public void Rect_PlainAttributes_AreParsed()
        {
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><rect x="10" y="20" width="30" height="40"/></svg>""");

            var rect = Assert.IsType<SvgRectElement>(Assert.Single(document.Children));
            Assert.Equal(10, rect.X);
            Assert.Equal(20, rect.Y);
            Assert.Equal(30, rect.Width);
            Assert.Equal(40, rect.Height);
            Assert.Equal(0, rect.Rx);
            Assert.Equal(0, rect.Ry);
        }

        [Fact]
        public void Rect_RxOnly_DefaultsRyToRx()
        {
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><rect x="0" y="0" width="30" height="40" rx="5"/></svg>""");

            var rect = Assert.IsType<SvgRectElement>(Assert.Single(document.Children));
            Assert.Equal(5, rect.Rx);
            Assert.Equal(5, rect.Ry);
        }

        [Fact]
        public void Rect_RyOnly_DefaultsRxToRy()
        {
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><rect x="0" y="0" width="30" height="40" ry="6"/></svg>""");

            var rect = Assert.IsType<SvgRectElement>(Assert.Single(document.Children));
            Assert.Equal(6, rect.Rx);
            Assert.Equal(6, rect.Ry);
        }

        [Fact]
        public void Rect_CornerRadii_AreClampedToHalfWidthAndHeight()
        {
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><rect x="0" y="0" width="20" height="10" rx="100" ry="100"/></svg>""");

            var rect = Assert.IsType<SvgRectElement>(Assert.Single(document.Children));
            Assert.Equal(10, rect.Rx);
            Assert.Equal(5, rect.Ry);
        }

        [Fact]
        public void Ellipse_Attributes_AreParsed()
        {
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><ellipse cx="10" cy="20" rx="5" ry="8"/></svg>""");

            var ellipse = Assert.IsType<SvgEllipseElement>(Assert.Single(document.Children));
            Assert.Equal(10, ellipse.Cx);
            Assert.Equal(20, ellipse.Cy);
            Assert.Equal(5, ellipse.Rx);
            Assert.Equal(8, ellipse.Ry);
        }

        [Fact]
        public void Line_Attributes_AreParsed()
        {
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><line x1="1" y1="2" x2="3" y2="4"/></svg>""");

            var line = Assert.IsType<SvgLineElement>(Assert.Single(document.Children));
            Assert.Equal(1, line.X1);
            Assert.Equal(2, line.Y1);
            Assert.Equal(3, line.X2);
            Assert.Equal(4, line.Y2);
        }

        [Fact]
        public void Polyline_Points_AreParsed()
        {
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><polyline points="0,0 10,0 10,10"/></svg>""");

            var polyline = Assert.IsType<SvgPolylineElement>(Assert.Single(document.Children));
            Assert.Equal(3, polyline.Points.Length);
        }

        [Fact]
        public void NestedSvg_ExplicitSize_IsUsed()
        {
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
                  <svg x="10" y="20" width="30" height="40" viewBox="0 0 15 20"><circle cx="5" cy="5" r="5"/></svg>
                </svg>
                """);

            var nested = Assert.IsType<SvgNestedSvgElement>(Assert.Single(document.Children));
            Assert.Equal(10, nested.X);
            Assert.Equal(20, nested.Y);
            Assert.Equal(30, nested.Width);
            Assert.Equal(40, nested.Height);
            Assert.NotNull(nested.ViewBox);
            Assert.Single(nested.Children);
        }

        [Fact]
        public void NestedSvg_MissingSize_DefaultsToEnclosingViewport()
        {
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 150">
                  <svg><circle cx="5" cy="5" r="5"/></svg>
                </svg>
                """);

            var nested = Assert.IsType<SvgNestedSvgElement>(Assert.Single(document.Children));
            Assert.Equal(200, nested.Width);
            Assert.Equal(150, nested.Height);
        }

        [Fact]
        public void Symbol_ReferencedByUse_BuildsSymbolElementWithChildren()
        {
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <defs><symbol id="icon" viewBox="0 0 10 10"><circle cx="5" cy="5" r="5"/></symbol></defs>
                  <use href="#icon" x="1" y="2" width="20" height="30"/>
                </svg>
                """);

            var use = Assert.IsType<SvgUseElement>(Assert.Single(document.Children));
            Assert.Equal(1, use.X);
            Assert.Equal(2, use.Y);
            Assert.Equal(20, use.Width);
            Assert.Equal(30, use.Height);

            var symbol = Assert.IsType<SvgSymbolElement>(use.Target);
            Assert.NotNull(symbol.ViewBox);
            Assert.Single(symbol.Children);
        }

        [Fact]
        public void Symbol_NotReferencedDirectly_IsNotPaintedAsOrdinaryChild()
        {
            // Like <defs>, a <symbol> appearing as a plain child (not via <use>) must not be painted.
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <symbol id="icon"><circle cx="5" cy="5" r="5"/></symbol>
                </svg>
                """);

            Assert.Empty(document.Children);
        }

        [Fact]
        public void Use_WidthHeight_OnlyResolvedForSymbolOrNestedSvgTargets()
        {
            // width/height on <use> of an ordinary shape (not symbol/nested-svg) are parsed but have
            // no rendering effect - SvgUseElement still records them, the renderer is what ignores them.
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <defs><circle id="c" cx="5" cy="5" r="5"/></defs>
                  <use href="#c" width="99" height="99"/>
                </svg>
                """);

            var use = Assert.IsType<SvgUseElement>(Assert.Single(document.Children));
            Assert.Equal(99, use.Width);
            Assert.IsType<SvgCircleElement>(use.Target);
        }

        [Fact]
        public void LinearGradient_NoGradientUnitsAttribute_DefaultsToObjectBoundingBox()
        {
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <defs><linearGradient id="g"><stop offset="0" stop-color="#000"/><stop offset="1" stop-color="#fff"/></linearGradient></defs>
                  <rect x="0" y="0" width="10" height="10" fill="url(#g)"/>
                </svg>
                """);

            var gradient = Assert.IsType<SvgLinearGradient>(document.Gradients["g"]);
            Assert.False(gradient.GradientUnitsUserSpaceOnUse);
            // Spec default: x1/y1 = 0%, x2 = 100%, y2 = 0% -> expressed as bare 0/1 fractions.
            Assert.Equal(0, gradient.X1);
            Assert.Equal(0, gradient.Y1);
            Assert.Equal(1, gradient.X2);
            Assert.Equal(0, gradient.Y2);
        }

        [Fact]
        public void RadialGradient_NoGradientUnitsAttribute_DefaultsToObjectBoundingBoxFiftyPercent()
        {
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <defs><radialGradient id="g"><stop offset="0" stop-color="#000"/><stop offset="1" stop-color="#fff"/></radialGradient></defs>
                  <rect x="0" y="0" width="10" height="10" fill="url(#g)"/>
                </svg>
                """);

            var gradient = Assert.IsType<SvgRadialGradient>(document.Gradients["g"]);
            Assert.False(gradient.GradientUnitsUserSpaceOnUse);
            Assert.Equal(0.5, gradient.Cx);
            Assert.Equal(0.5, gradient.Cy);
            Assert.Equal(0.5, gradient.R);
            Assert.Null(gradient.Fx);
            Assert.Null(gradient.Fy);
        }

        [Fact]
        public void RadialGradient_FxFy_AreParsed()
        {
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <defs><radialGradient id="g" gradientUnits="userSpaceOnUse" cx="50" cy="50" r="40" fx="30" fy="20">
                    <stop offset="0" stop-color="#000"/><stop offset="1" stop-color="#fff"/>
                  </radialGradient></defs>
                  <rect x="0" y="0" width="10" height="10" fill="url(#g)"/>
                </svg>
                """);

            var gradient = Assert.IsType<SvgRadialGradient>(document.Gradients["g"]);
            Assert.Equal(30, gradient.Fx);
            Assert.Equal(20, gradient.Fy);
        }

        [Fact]
        public void Gradient_SpreadMethod_IsParsed()
        {
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <defs><linearGradient id="g" spreadMethod="reflect"><stop offset="0" stop-color="#000"/><stop offset="1" stop-color="#fff"/></linearGradient></defs>
                  <rect x="0" y="0" width="10" height="10" fill="url(#g)"/>
                </svg>
                """);

            Assert.Equal(SvgSpreadMethod.Reflect, document.Gradients["g"].SpreadMethod);
        }

        [Fact]
        public void CurrentColor_ResolvesToContextColorPassedToBuild()
        {
            var xdoc = XDocument.Parse("""<svg xmlns="http://www.w3.org/2000/svg"><circle cx="5" cy="5" r="5" fill="currentColor"/></svg>""");
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(xdoc.Root!), Adapter, RColor.FromArgb(0x10, 0x20, 0x30));

            var circle = Assert.IsType<SvgCircleElement>(Assert.Single(document.Children));
            Assert.Equal(SvgPaintKind.Solid, circle.Fill.Kind);
            Assert.Equal(RColor.FromArgb(0x10, 0x20, 0x30), circle.Fill.Color);
        }

        [Fact]
        public void CurrentColor_WithNoContextColor_DefaultsToBlack()
        {
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><circle cx="5" cy="5" r="5" fill="currentColor"/></svg>""");

            var circle = Assert.IsType<SvgCircleElement>(Assert.Single(document.Children));
            Assert.Equal(RColor.Black, circle.Fill.Color);
        }

        [Fact]
        public void Style_OverridesPresentationAttribute()
        {
            // Per CSS precedence, style="" wins over a same-named presentation attribute.
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><circle cx="5" cy="5" r="5" fill="#ff0000" style="fill: #00ff00"/></svg>""");

            var circle = Assert.IsType<SvgCircleElement>(Assert.Single(document.Children));
            Assert.Equal(RColor.FromArgb(0x00, 0xff, 0x00), circle.Fill.Color);
        }

        [Fact]
        public void Style_FallsBackToPresentationAttributeWhenPropertyNotStyled()
        {
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><circle cx="5" cy="5" r="5" fill="#ff0000" style="stroke: #0000ff"/></svg>""");

            var circle = Assert.IsType<SvgCircleElement>(Assert.Single(document.Children));
            Assert.Equal(RColor.FromArgb(0xff, 0x00, 0x00), circle.Fill.Color);
            Assert.Equal(RColor.FromArgb(0x00, 0x00, 0xff), circle.Stroke.Color);
        }

        [Fact]
        public void Style_MultipleProperties_AllApply()
        {
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><path d="M0,0 L10,0" style="stroke: #ff0000; stroke-width: 3; fill-rule: evenodd"/></svg>""");

            var path = Assert.IsType<SvgPathElement>(Assert.Single(document.Children));
            Assert.Equal(RColor.FromArgb(0xff, 0x00, 0x00), path.Stroke.Color);
            Assert.Equal(3, path.StrokeWidth);
            Assert.Equal(RFillMode.EvenOdd, path.FillRule);
        }

        [Fact]
        public void StyleElement_ClassSelector_AppliesToMatchingShape_FromCssBoxTree()
        {
            // Uses HtmlParser.ParseDocument directly (bypassing DomParser's full cascade/
            // CorrectReplacedElementBoxes pipeline) specifically to isolate SvgTreeBuilder's own
            // <style>-handling correctness from a separate, pre-existing bug: MimeKit's HTML
            // tokenizer currently hoists a <style> tag out of a nested inline <svg> (making <svg>'s
            // own CssBox never get constructed as CssBoxSvg at all), so a <style>-inside-inline-<svg>
            // integration test through the *full* PdfGenerator pipeline would fail for a reason
            // unrelated to SVG - see ImgSvg_StyleElement_ClassSelectorAppliesToShape in
            // SvgIntegrationTests.cs for the equivalent full-pipeline proof via standalone/<img> SVG,
            // which parses as real XML and isn't affected by this HTML-tokenizer quirk.
            var root = HtmlParser.ParseDocument("""
                <!DOCTYPE html><html><body>
                <svg xmlns="http://www.w3.org/2000/svg">
                  <style>.highlight { fill: #ff0000; }</style>
                  <circle class="highlight" cx="5" cy="5" r="5"/>
                </svg>
                </body></html>
                """);
            var svgBox = DomUtils.GetBoxByTagName(root, "svg");
            var document = SvgTreeBuilder.Build(new CssBoxSvgSourceNode(svgBox!), Adapter);

            var circle = Assert.IsType<SvgCircleElement>(Assert.Single(document.Children));
            Assert.Equal(RColor.FromArgb(0xff, 0x00, 0x00), circle.Fill.Color);
        }

        [Fact]
        public void StyleElement_ClassSelector_AppliesToMatchingShape_FromXmlDocument()
        {
            var xdoc = XDocument.Parse("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <style>.highlight { fill: #ff0000; }</style>
                  <circle class="highlight" cx="5" cy="5" r="5"/>
                </svg>
                """);
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(xdoc.Root!), Adapter);

            var circle = Assert.IsType<SvgCircleElement>(Assert.Single(document.Children));
            Assert.Equal(RColor.FromArgb(0xff, 0x00, 0x00), circle.Fill.Color);
        }

        [Fact]
        public void StyleElement_UnmatchedShape_IsNotPaintedAsAShapeItself()
        {
            // <style> itself must not be interpreted as a paintable element by BuildElement's dispatch.
            var document = BuildFrom("""<svg xmlns="http://www.w3.org/2000/svg"><style>.foo { fill: red; }</style></svg>""");
            Assert.Empty(document.Children);
        }

        [Fact]
        public void StyleElement_IdSelector_BeatsClassSelectorSpecificity()
        {
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <style>.foo { fill: #ff0000; } #bar { fill: #0000ff; }</style>
                  <circle id="bar" class="foo" cx="5" cy="5" r="5"/>
                </svg>
                """);

            var circle = Assert.IsType<SvgCircleElement>(Assert.Single(document.Children));
            Assert.Equal(RColor.FromArgb(0x00, 0x00, 0xff), circle.Fill.Color);
        }

        [Fact]
        public void StyleElement_InlineStyleAttribute_OverridesStyleElementRule()
        {
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <style>.foo { fill: #ff0000; }</style>
                  <circle class="foo" cx="5" cy="5" r="5" style="fill: #00ff00"/>
                </svg>
                """);

            var circle = Assert.IsType<SvgCircleElement>(Assert.Single(document.Children));
            Assert.Equal(RColor.FromArgb(0x00, 0xff, 0x00), circle.Fill.Color);
        }

        [Fact]
        public void StyleElement_PresentationAttribute_LosesToStyleElementRule()
        {
            // Per CSS precedence, an author stylesheet rule (however it was authored) beats a bare
            // presentation attribute, even though the attribute is textually "closer" to the element.
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <style>.foo { fill: #0000ff; }</style>
                  <circle class="foo" fill="#ff0000" cx="5" cy="5" r="5"/>
                </svg>
                """);

            var circle = Assert.IsType<SvgCircleElement>(Assert.Single(document.Children));
            Assert.Equal(RColor.FromArgb(0x00, 0x00, 0xff), circle.Fill.Color);
        }

        [Fact]
        public void Rect_UsedAsClipPathShape_ContributesGeometry()
        {
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <defs><clipPath id="clip"><rect x="0" y="0" width="10" height="10"/></clipPath></defs>
                  <circle clip-path="url(#clip)" cx="5" cy="5" r="5"/>
                </svg>
                """);

            var clipPath = document.ClipPaths["clip"];
            Assert.IsType<SvgRectElement>(Assert.Single(clipPath.Shapes));
        }

        [Fact]
        public void Switch_RendersOnlyFirstBuildableChild()
        {
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <switch>
                    <circle cx="1" cy="1" r="1"/>
                    <rect x="0" y="0" width="5" height="5"/>
                  </switch>
                </svg>
                """);

            Assert.IsType<SvgCircleElement>(Assert.Single(document.Children));
        }

        [Fact]
        public void Switch_SkipsUnbuildableChildrenToFindFirstBuildableOne()
        {
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <switch>
                    <foreignObject width="5" height="5"/>
                    <rect x="0" y="0" width="5" height="5"/>
                  </switch>
                </svg>
                """);

            Assert.IsType<SvgRectElement>(Assert.Single(document.Children));
        }

        [Fact]
        public void Switch_NoBuildableChildren_BuildsNothing()
        {
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <switch><foreignObject width="5" height="5"/></switch>
                </svg>
                """);

            Assert.Empty(document.Children);
        }

        [Fact]
        public void Anchor_ParsesHrefAndBuildsChildren()
        {
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <a href="https://example.com">
                    <rect x="0" y="0" width="5" height="5"/>
                  </a>
                </svg>
                """);

            var anchor = Assert.IsType<SvgAnchorElement>(Assert.Single(document.Children));
            Assert.Equal("https://example.com", anchor.Href);
            Assert.IsType<SvgRectElement>(Assert.Single(anchor.Children));
        }

        [Fact]
        public void Anchor_WithoutHref_StillBuildsChildren()
        {
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <a>
                    <rect x="0" y="0" width="5" height="5"/>
                  </a>
                </svg>
                """);

            var anchor = Assert.IsType<SvgAnchorElement>(Assert.Single(document.Children));
            Assert.Null(anchor.Href);
            Assert.IsType<SvgRectElement>(Assert.Single(anchor.Children));
        }

        [Fact]
        public void Anchor_FillInheritedByUnstyledChild()
        {
            var document = BuildFrom("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <a href="#x" fill="#00ff00">
                    <rect x="0" y="0" width="5" height="5"/>
                  </a>
                </svg>
                """);

            var anchor = Assert.IsType<SvgAnchorElement>(Assert.Single(document.Children));
            var rect = Assert.IsType<SvgRectElement>(Assert.Single(anchor.Children));
            Assert.Equal(RColor.FromArgb(0x00, 0xff, 0x00), rect.Fill.Color);
        }
    }
}
