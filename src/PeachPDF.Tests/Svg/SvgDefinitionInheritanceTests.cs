using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Svg;
using System.Linq;
using System.Xml.Linq;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Content inside <c>&lt;pattern&gt;</c>/<c>&lt;marker&gt;</c>/<c>&lt;mask&gt;</c>/<c>&lt;clipPath&gt;</c> (and a
    /// gradient's coordinates) inherits from the definition element's own ancestors - not from the element that
    /// references it (SVG 2 §14.3.1, §11.6.2; CSS Masking 1 §7.1). Each test states the inherited value as a literal.
    /// </summary>
    public class SvgDefinitionInheritanceTests
    {
        private static readonly PdfSharpAdapter Adapter = new();

        private static readonly RColor White = RColor.FromArgb(0xff, 0xff, 0xff);

        private static SvgDocument Build(string markup)
        {
            var root = XDocument.Parse(markup).Root!;
            return SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, null, "print"), Adapter);
        }

        private static SvgRectElement OnlyRect(IEnumerable<SvgElement> children) => Assert.IsType<SvgRectElement>(Assert.Single(children));

        [Fact]
        public void RootFill_IsInheritedByPatternContent()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg" fill="#fff">
                  <defs><pattern id="p" width="10" height="10" patternUnits="userSpaceOnUse"><rect width="5" height="5"/></pattern></defs>
                  <rect width="100" height="100" fill="url(#p)"/>
                </svg>
                """);

            var fill = OnlyRect(document.Patterns["p"].Children).Fill;
            Assert.Equal(SvgPaintKind.Solid, fill.Kind);
            Assert.Equal(White, fill.Color);
        }

        [Fact]
        public void RootFill_IsInheritedByMarkerMaskAndClipPathContent()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg" fill="#fff">
                  <defs>
                    <marker id="m"><rect width="2" height="2"/></marker>
                    <mask id="k"><rect width="10" height="10"/></mask>
                    <clipPath id="c"><rect width="10" height="10"/></clipPath>
                  </defs>
                </svg>
                """);

            Assert.Equal(White, OnlyRect(document.Markers["m"].Children).Fill.Color);
            Assert.Equal(White, OnlyRect(document.Masks["k"].Children).Fill.Color);
            Assert.Equal(White, OnlyRect(document.ClipPaths["c"].Shapes).Fill.Color);
        }

        [Fact]
        public void GroupAndDefsAncestors_ContributeToTheInheritedContext_InDocumentOrder()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg" fill="#ff0000" stroke="#0000ff">
                  <g fill="#ffffff">
                    <defs stroke="#00ff00"><mask id="k"><rect width="10" height="10"/></mask></defs>
                  </g>
                </svg>
                """);

            var rect = OnlyRect(document.Masks["k"].Children);
            Assert.Equal(White, rect.Fill.Color);                                  // <g> beats the root
            Assert.Equal(RColor.FromArgb(0x00, 0xff, 0x00), rect.Stroke.Color);   // <defs> beats the root
        }

        [Fact]
        public void TheDefinitionElementsOwnProperties_AreInheritedByItsContent()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg" fill="#ff0000">
                  <pattern id="p" width="10" height="10" patternUnits="userSpaceOnUse" fill="#00ff00"><rect width="5" height="5"/></pattern>
                </svg>
                """);

            Assert.Equal(RColor.FromArgb(0x00, 0xff, 0x00), OnlyRect(document.Patterns["p"].Children).Fill.Color);
        }

        [Fact]
        public void AChildsOwnFill_StillWinsOverTheInheritedOne()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg" fill="#fff">
                  <pattern id="p" width="10" height="10" patternUnits="userSpaceOnUse"><rect width="5" height="5" fill="#0000ff"/></pattern>
                </svg>
                """);

            Assert.Equal(RColor.FromArgb(0x00, 0x00, 0xff), OnlyRect(document.Patterns["p"].Children).Fill.Color);
        }

        [Fact]
        public void StrokeStrokeWidthAndFillOpacity_AreInherited()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg" stroke="#0000ff" stroke-width="3" fill-opacity="0.5">
                  <marker id="m"><rect width="2" height="2"/></marker>
                </svg>
                """);

            var rect = OnlyRect(document.Markers["m"].Children);
            Assert.Equal(RColor.FromArgb(0x00, 0x00, 0xff), rect.Stroke.Color);
            Assert.Equal(3, rect.StrokeWidth);
            Assert.Equal(0.5, rect.FillOpacity);
        }

        [Fact]
        public void WithNothingToInherit_ContentStillStartsFromTheInitialValues()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <pattern id="p" width="10" height="10" patternUnits="userSpaceOnUse"><rect width="5" height="5"/></pattern>
                </svg>
                """);

            var rect = OnlyRect(document.Patterns["p"].Children);
            Assert.Equal(RColor.Black, rect.Fill.Color);
            Assert.Equal(SvgPaintKind.None, rect.Stroke.Kind);
            Assert.Equal(1, rect.StrokeWidth);
        }

        [Fact]
        public void PercentageStrokeWidth_ResolvesAgainstTheViewportTheDefinitionSitsIn()
        {
            // The rect's own attribute lengths use the enclosing viewport (here the nested <svg>'s 50x50 viewBox, not
            // the root's 200x200): 10% of the diagonal-normalized 50 is 5, where the root's would give 20.
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                  <svg width="100" height="100" viewBox="0 0 50 50">
                    <pattern id="p" width="10" height="10" patternUnits="userSpaceOnUse"><rect width="5" height="5" stroke-width="10%"/></pattern>
                  </svg>
                </svg>
                """);

            Assert.Equal(5, OnlyRect(document.Patterns["p"].Children).StrokeWidth, 6);
        }

        [Fact]
        public void PercentageStrokeWidth_InsideASymbol_ResolvesAgainstTheSymbolsViewBox()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                  <symbol id="s" viewBox="0 0 50 50">
                    <pattern id="p" width="10" height="10" patternUnits="userSpaceOnUse"><rect width="5" height="5" stroke-width="10%"/></pattern>
                  </symbol>
                </svg>
                """);

            Assert.Equal(5, OnlyRect(document.Patterns["p"].Children).StrokeWidth, 6);
        }

        [Fact]
        public void PercentageStrokeWidth_InheritedFromAnAncestor_ResolvesAgainstThatAncestorsViewport()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 200">
                  <g stroke-width="10%"><pattern id="p" width="10" height="10" patternUnits="userSpaceOnUse"><rect width="5" height="5"/></pattern></g>
                </svg>
                """);

            Assert.Equal(20, OnlyRect(document.Patterns["p"].Children).StrokeWidth, 6);
        }

        [Fact]
        public void Em_InDefinitionContent_FollowsAnAncestorsFontSize()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <g font-size="20"><defs><pattern id="p" width="10" height="10" patternUnits="userSpaceOnUse"><rect width="2em" height="1"/></pattern></defs></g>
                </svg>
                """);

            Assert.Equal(40, OnlyRect(document.Patterns["p"].Children).Width, 6);
        }

        [Fact]
        public void Rem_InMarkerContent_FollowsTheRootFontSize()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg" font-size="10">
                  <g font-size="30"><marker id="m"><rect width="3rem" height="1"/></marker></g>
                </svg>
                """);

            Assert.Equal(30, OnlyRect(document.Markers["m"].Children).Width, 6);
        }

        [Fact]
        public void Em_InClipPathContent_FollowsAnAncestorsFontSize()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <g font-size="20"><clipPath id="c"><rect width="2em" height="1"/></clipPath></g>
                </svg>
                """);

            Assert.Equal(40, OnlyRect(document.ClipPaths["c"].Shapes).Width, 6);
        }

        [Fact]
        public void Em_OnTheDefinitionElementItself_UsesItsOwnFontSize()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg" font-size="20">
                  <pattern id="p" patternUnits="userSpaceOnUse" width="4em" height="10" font-size="10"><rect width="1em" height="1"/></pattern>
                  <marker id="m" markerWidth="2em" markerHeight="3" font-size="10"/>
                </svg>
                """);

            var pattern = document.Patterns["p"];
            Assert.Equal(40, pattern.Width, 6);
            Assert.Equal(10, OnlyRect(pattern.Children).Width, 6); // the pattern's own font-size (10) is what its children inherit
            Assert.Equal(20, document.Markers["m"].MarkerWidth, 6);
        }

        [Fact]
        public void Em_InUserSpaceGradientCoordinates_FollowsAnAncestorsFontSize()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <g font-size="20">
                    <linearGradient id="g" gradientUnits="userSpaceOnUse" x1="1em" x2="2em"><stop offset="0" stop-color="#000"/></linearGradient>
                    <radialGradient id="r" gradientUnits="userSpaceOnUse" cx="1em" cy="1em" r="3em"/>
                  </g>
                </svg>
                """);

            var linear = Assert.IsType<SvgLinearGradient>(document.Gradients["g"]);
            Assert.Equal(20, linear.X1, 6);
            Assert.Equal(40, linear.X2, 6);

            var radial = Assert.IsType<SvgRadialGradient>(document.Gradients["r"]);
            Assert.Equal(20, radial.Cx, 6);
            Assert.Equal(20, radial.Cy, 6);
            Assert.Equal(60, radial.R, 6);
        }

        [Fact]
        public void ObjectBoundingBoxGradientCoordinates_AreUnchanged()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg" font-size="20">
                  <linearGradient id="g" x2="50%"/>
                </svg>
                """);

            Assert.Equal(0.5, Assert.IsType<SvgLinearGradient>(document.Gradients["g"]).X2, 6);
        }

        [Fact]
        public void APatternReferencedFromEarlierDefinitionContent_IsClassifiedAsAPattern()
        {
            // The pattern "b" is defined after the marker and pattern that paint with it.
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <marker id="m"><rect width="2" height="2" fill="url(#b)"/></marker>
                  <pattern id="a" width="10" height="10" patternUnits="userSpaceOnUse"><rect width="5" height="5" fill="url(#b)"/></pattern>
                  <pattern id="b" width="4" height="4" patternUnits="userSpaceOnUse"><rect width="2" height="2"/></pattern>
                </svg>
                """);

            Assert.Equal(SvgPaintKind.PatternRef, OnlyRect(document.Markers["m"].Children).Fill.Kind);
            Assert.Equal(SvgPaintKind.PatternRef, OnlyRect(document.Patterns["a"].Children).Fill.Kind);
            Assert.Equal("b", OnlyRect(document.Patterns["a"].Children).Fill.ReferenceId);
        }

        [Fact]
        public void APatternInheritingItselfAsItsOwnFill_DoesNotPaintItself()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg" fill="url(#p)" stroke="url(#p)">
                  <pattern id="p" width="10" height="10" patternUnits="userSpaceOnUse"><rect width="5" height="5"/></pattern>
                  <rect width="100" height="100"/>
                </svg>
                """);

            var inside = OnlyRect(document.Patterns["p"].Children);
            Assert.Equal(SvgPaintKind.None, inside.Fill.Kind);
            Assert.Equal(SvgPaintKind.None, inside.Stroke.Kind);

            // The ordinary rect still paints with the pattern, as authored.
            Assert.Equal(SvgPaintKind.PatternRef, Assert.IsType<SvgRectElement>(Assert.Single(document.Children.OfType<SvgRectElement>())).Fill.Kind);
        }

        [Fact]
        public void MarkerContent_DoesNotInheritMarkerReferences()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg" marker-end="url(#m)">
                  <marker id="m"><path d="M0,0 L2,2"/></marker>
                  <path d="M0,0 L10,10"/>
                </svg>
                """);

            var inside = Assert.IsType<SvgPathElement>(Assert.Single(document.Markers["m"].Children));
            Assert.Null(inside.MarkerEndRef);

            // ...while an ordinary path inherits it as usual.
            Assert.Equal("m", Assert.IsType<SvgPathElement>(Assert.Single(document.Children.OfType<SvgPathElement>())).MarkerEndRef);
        }

        [Fact]
        public void MarkerContent_StillInheritsAReferenceToADifferentMarker()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg" marker-end="url(#other)">
                  <marker id="other"><circle r="1"/></marker>
                  <marker id="m"><path d="M0,0 L2,2"/></marker>
                </svg>
                """);

            Assert.Equal("other", Assert.IsType<SvgPathElement>(Assert.Single(document.Markers["m"].Children)).MarkerEndRef);
        }

        [Fact]
        public void SiblingDefinitionsUnderOneAncestor_AllSeeTheSameInheritedContext()
        {
            // Exercises the memoized ancestor replay: the second and third definitions resume from the cached <g>.
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <g fill="#fff" font-size="20">
                    <pattern id="a" width="1" height="1"><rect width="1em" height="1"/></pattern>
                    <pattern id="b" width="1" height="1"><rect width="2em" height="1"/></pattern>
                    <mask id="c"><rect width="10" height="10"/></mask>
                  </g>
                </svg>
                """);

            Assert.Equal(20, OnlyRect(document.Patterns["a"].Children).Width, 6);
            Assert.Equal(40, OnlyRect(document.Patterns["b"].Children).Width, 6);
            Assert.Equal(White, OnlyRect(document.Patterns["b"].Children).Fill.Color);
            Assert.Equal(White, OnlyRect(document.Masks["c"].Children).Fill.Color);
        }

        [Fact]
        public void AClipPathInsideAnElementThatIsClippedByIt_IsResolvedOnce_WithoutRecursing()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <g clip-path="url(#c)" fill="#fff"><clipPath id="c"><rect width="10" height="10" clip-path="url(#c)"/></clipPath></g>
                </svg>
                """);

            Assert.Equal(White, OnlyRect(document.ClipPaths["c"].Shapes).Fill.Color);
        }

        [Fact]
        public void ADefinitionInsideAnotherDefinition_InheritsThroughTheOuterOne()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg" fill="#ff0000">
                  <pattern id="outer" width="10" height="10" patternUnits="userSpaceOnUse" fill="#fff">
                    <marker id="inner"><rect width="2" height="2"/></marker>
                  </pattern>
                </svg>
                """);

            Assert.Equal(White, OnlyRect(document.Markers["inner"].Children).Fill.Color);
        }

        [Fact]
        public void UseStillInheritsFromTheUseElement_NotFromTheDefinitionsAncestors()
        {
            var document = Build("""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <defs><g fill="#ff0000"><rect id="r" width="5" height="5"/></g></defs>
                  <use href="#r" fill="#fff"/>
                </svg>
                """);

            var use = Assert.IsType<SvgUseElement>(Assert.Single(document.Children));
            Assert.Equal(White, Assert.IsType<SvgRectElement>(use.Target).Fill.Color);
        }
    }
}
