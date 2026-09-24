using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Svg;
using System.Linq;
using System.Xml.Linq;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Proves <c>SvgTreeBuilder.BuildFilter</c>'s parse-time acceptance/rejection rules: which
    /// <c>&lt;filter&gt;</c> graphs are natively representable (and get registered into
    /// <see cref="SvgDocument.Filters"/>) versus rejected as a WHOLE (never registered at all - see
    /// <see cref="SvgFilter"/>'s remarks for why this is whole-filter, not partial-graph, rejection).
    /// </summary>
    public class SvgFilterTreeBuilderTests
    {
        private static readonly PdfSharpAdapter Adapter = new();

        private static SvgDocument BuildFrom(string filterMarkup)
        {
            var markup = $"""<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><defs>{filterMarkup}</defs><rect id="target" width="50" height="50" filter="url(#f)"/></svg>""";
            var root = XDocument.Parse(markup).Root!;
            return SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, null, "print"), Adapter);
        }

        [Fact]
        public void SimpleSupportedGraph_IsRegistered()
        {
            var document = BuildFrom("""<filter id="f"><feFlood flood-color="red"/><feOffset dx="2" dy="3"/></filter>""");

            Assert.True(document.Filters.ContainsKey("f"));
            var filter = document.Filters["f"];
            Assert.Equal(2, filter.Primitives.Count);
            Assert.IsType<FeFlood>(filter.Primitives[0]);
            var offset = Assert.IsType<FeOffset>(filter.Primitives[1]);
            Assert.Equal(2, offset.Dx);
            Assert.Equal(3, offset.Dy);
        }

        [Fact]
        public void ReferencingElement_GetsFilterRef()
        {
            var document = BuildFrom("""<filter id="f"><feFlood/></filter>""");

            var target = Assert.IsType<SvgRectElement>(Assert.Single(document.Children));
            Assert.Equal("f", target.FilterRef);
        }

        [Fact]
        public void DefaultRegion_IsMinus10PercentPlus120Percent_ObjectBoundingBox()
        {
            var document = BuildFrom("""<filter id="f"><feFlood/></filter>""");

            var filter = document.Filters["f"];
            Assert.False(filter.FilterUnitsUserSpaceOnUse);
            Assert.Equal(-0.1, filter.X);
            Assert.Equal(-0.1, filter.Y);
            Assert.Equal(1.2, filter.Width);
            Assert.Equal(1.2, filter.Height);
        }

        [Fact]
        public void UserSpaceOnUse_ResolvesLiteralRegionValues()
        {
            var document = BuildFrom("""<filter id="f" filterUnits="userSpaceOnUse" x="1" y="2" width="30" height="40"><feFlood/></filter>""");

            var filter = document.Filters["f"];
            Assert.True(filter.FilterUnitsUserSpaceOnUse);
            Assert.Equal(1, filter.X);
            Assert.Equal(2, filter.Y);
            Assert.Equal(30, filter.Width);
            Assert.Equal(40, filter.Height);
        }

        [Theory]
        [InlineData("""<filter id="f"><feImage href="#x"/></filter>""")]
        [InlineData("""<filter id="f"><feComposite operator="bogus"/></filter>""")]
        [InlineData("""<filter id="f"><feFlood in="BackgroundImage"/></filter>""")]
        [InlineData("""<filter id="f"><feOffset in="StrokePaint" dx="1" dy="1"/></filter>""")]
        [InlineData("""<filter id="f"><feMerge><feMergeNode in="BackgroundAlpha"/></feMerge></filter>""")]
        [InlineData("""<filter id="f"><feTile in="FillPaint"/></filter>""")]
        [InlineData("""<filter id="f"><feBlend in2="BackgroundImage" mode="multiply"/></filter>""")]
        [InlineData("""<filter id="f"><feComposite in2="FillPaint"/></filter>""")]
        public void UnsupportedGraph_IsNeverRegistered(string filterMarkup)
        {
            var document = BuildFrom(filterMarkup);

            Assert.False(document.Filters.ContainsKey("f"));

            // Whole-filter rejection: the referencing element still gets its FilterRef parsed (same shape
            // as an unresolved mask/clip-path reference), it just resolves to nothing at render time - it
            // is never a partially-applied graph.
            var target = Assert.IsType<SvgRectElement>(Assert.Single(document.Children));
            Assert.Equal("f", target.FilterRef);
        }

        [Theory]
        [InlineData("""<filter id="f"><feGaussianBlur stdDeviation="2"/></filter>""")]
        [InlineData("""<filter id="f"><feComposite operator="arithmetic" k1="1" k2="0" k3="0" k4="0"/></filter>""")]
        [InlineData("""<filter id="f"><feColorMatrix type="saturate" values="0.5"/></filter>""")]
        [InlineData("""<filter id="f"><feColorMatrix type="hueRotate" values="90"/></filter>""")]
        // Off-diagonal type="matrix": R' picks up some G (a cross-channel coupling), so IsChannelIndependent is false.
        [InlineData("""<filter id="f"><feColorMatrix type="matrix" values="1 0.2 0 0 0  0 1 0 0 0  0 0 1 0 0  0 0 0 1 0"/></filter>""")]
        [InlineData("""<filter id="f"><feComponentTransfer><feFuncR type="gamma" amplitude="1" exponent="2" offset="0"/></feComponentTransfer></filter>""")]
        [InlineData("""<filter id="f"><feComponentTransfer><feFuncR type="table" tableValues="0 1"/></feComponentTransfer></filter>""")]
        [InlineData("""<filter id="f"><feComponentTransfer><feFuncR type="discrete" tableValues="0 1"/></feComponentTransfer></filter>""")]
        [InlineData("""<filter id="f"><feComponentTransfer><feFuncA type="linear" slope="0.5"/></feComponentTransfer></filter>""")]
        [InlineData("""<filter id="f"><feFlood x="0" y="0" width="10" height="10"/></filter>""")] // per-primitive subregion
        [InlineData("""<filter id="f"><feMorphology operator="dilate" radius="2"/></filter>""")]
        [InlineData("""<filter id="f"><feConvolveMatrix order="3" kernelMatrix="0 0 0 0 1 0 0 0 0"/></filter>""")]
        [InlineData("""<filter id="f"><feTurbulence baseFrequency="0.05" numOctaves="2"/></filter>""")]
        [InlineData("""<filter id="f"><feDisplacementMap scale="5" xChannelSelector="R" yChannelSelector="G"/></filter>""")]
        [InlineData("""<filter id="f"><feDiffuseLighting><feDistantLight azimuth="45" elevation="45"/></feDiffuseLighting></filter>""")]
        [InlineData("""<filter id="f"><feSpecularLighting specularExponent="8"><fePointLight x="10" y="10" z="20"/></feSpecularLighting></filter>""")]
        [InlineData("""<filter id="f"><feDropShadow dx="2" dy="2" stdDeviation="1"/></filter>""")]
        public void PixelPrimitives_AreRegistered_AndMarkTheFilterAsNeedingRaster(string filterMarkup)
        {
            var document = BuildFrom(filterMarkup);

            Assert.True(document.Filters.ContainsKey("f"));
            Assert.True(document.Filters["f"].RequiresRaster);
        }

        [Fact]
        public void VectorOnlyPrimitives_DoNotRequireRaster()
        {
            var document = BuildFrom("""<filter id="f"><feFlood flood-color="red"/><feOffset dx="1" dy="2"/><feComponentTransfer><feFuncR type="linear" slope="2"/></feComponentTransfer></filter>""");

            Assert.False(document.Filters["f"].RequiresRaster);
        }

        [Theory]
        [InlineData("""<filter id="f"><feConvolveMatrix order="3" kernelMatrix="1 2"/></filter>""")] // wrong kernel length
        [InlineData("""<filter id="f"><feConvolveMatrix order="0" kernelMatrix=""/></filter>""")]
        [InlineData("""<filter id="f"><feConvolveMatrix order="3" targetX="5" kernelMatrix="0 0 0 0 1 0 0 0 0"/></filter>""")]
        [InlineData("""<filter id="f"><feTurbulence baseFrequency="-1"/></filter>""")]
        [InlineData("""<filter id="f"><feDiffuseLighting/></filter>""")] // no light source
        [InlineData("""<filter id="f"><feGaussianBlur stdDeviation="1 2 3"/></filter>""")]
        [InlineData("""<filter id="f"><feColorMatrix type="matrix" values="1 2 3"/></filter>""")]
        public void MalformedPixelPrimitives_RejectTheFilter(string filterMarkup)
        {
            Assert.False(BuildFrom(filterMarkup).Filters.ContainsKey("f"));
        }

        [Fact]
        public void ColorInterpolationFilters_DefaultsToLinearRgb_AndInheritsFromTheFilter()
        {
            var document = BuildFrom("""<filter id="f" color-interpolation-filters="sRGB"><feGaussianBlur stdDeviation="1"/><feOffset color-interpolation-filters="linearRGB"/></filter>""");

            var primitives = document.Filters["f"].Primitives;
            Assert.False(primitives[0].LinearRgb);
            Assert.True(primitives[1].LinearRgb);
            Assert.True(BuildFrom("""<filter id="f"><feGaussianBlur stdDeviation="1"/></filter>""").Filters["f"].Primitives[0].LinearRgb);
        }

        [Fact]
        public void FeColorMatrix_LuminanceToAlpha_IsAccepted()
        {
            var document = BuildFrom("""<filter id="f"><feColorMatrix type="luminanceToAlpha"/></filter>""");

            Assert.True(document.Filters.ContainsKey("f"));
            var primitive = Assert.IsType<FeColorMatrix>(Assert.Single(document.Filters["f"].Primitives));
            Assert.True(primitive.IsLuminanceToAlpha);
        }

        [Fact]
        public void FeComponentTransfer_LinearFunctions_AreAccepted_AndComposeADiagonalMatrix()
        {
            var document = BuildFrom("""
                <filter id="f">
                    <feComponentTransfer>
                        <feFuncR type="linear" slope="1.5" intercept="0.1"/>
                        <feFuncG type="linear" slope="2"/>
                    </feComponentTransfer>
                </filter>
                """);

            Assert.True(document.Filters.ContainsKey("f"));
            var primitive = Assert.IsType<FeComponentTransfer>(Assert.Single(document.Filters["f"].Primitives));

            var result = primitive.Matrix.Apply(new System.Numerics.Vector4(1, 1, 1, 1));
            Assert.Equal(1.6f, result.X, 5); // 1.5*1 + 0.1
            Assert.Equal(2f, result.Y, 5);   // 2*1 + 0 (default intercept)
            Assert.Equal(1f, result.Z, 5);   // feFuncB absent - identity
            Assert.Equal(1f, result.W, 5);   // alpha untouched
            Assert.True(primitive.Matrix.IsChannelIndependent);
        }

        [Fact]
        public void FeComponentTransfer_FeFuncA_Identity_IsAccepted()
        {
            var document = BuildFrom("""
                <filter id="f">
                    <feComponentTransfer>
                        <feFuncA type="identity"/>
                    </feComponentTransfer>
                </filter>
                """);

            Assert.True(document.Filters.ContainsKey("f"));
        }

        [Fact]
        public void FeComposite_EachSupportedOperator_IsAccepted()
        {
            foreach (var op in new[] { "over", "in", "out", "atop", "xor" })
            {
                var document = BuildFrom($"""<filter id="f"><feComposite operator="{op}"/></filter>""");
                var primitive = Assert.IsType<FeComposite>(Assert.Single(document.Filters["f"].Primitives));
                Assert.Equal(op, primitive.Operator);
            }
        }

        [Fact]
        public void FeMerge_BuildsInputsFromMergeNodeChildren_InDocumentOrder()
        {
            var document = BuildFrom("""
                <filter id="f">
                    <feFlood result="a"/>
                    <feFlood result="b"/>
                    <feMerge>
                        <feMergeNode in="a"/>
                        <feMergeNode in="b"/>
                    </feMerge>
                </filter>
                """);

            var merge = Assert.IsType<FeMerge>(document.Filters["f"].Primitives[2]);
            Assert.Equal(["a", "b"], merge.Inputs);
        }

        [Theory]
        [InlineData("multiply", "Multiply")]
        [InlineData("screen", "Screen")]
        [InlineData("overlay", "Overlay")]
        [InlineData("darken", "Darken")]
        [InlineData("lighten", "Lighten")]
        [InlineData("color-dodge", "ColorDodge")]
        [InlineData("color-burn", "ColorBurn")]
        [InlineData("hard-light", "HardLight")]
        [InlineData("soft-light", "SoftLight")]
        [InlineData("difference", "Difference")]
        [InlineData("exclusion", "Exclusion")]
        [InlineData("hue", "Hue")]
        [InlineData("saturation", "Saturation")]
        [InlineData("color", "Color")]
        [InlineData("luminosity", "Luminosity")]
        [InlineData("not-a-real-mode", "Normal")]
        // RBlendMode is internal, and a public [Theory] method can't take one as a parameter (CS0051) -
        // InlineData carries the expected mode's own name instead, matched via ToString().
        public void FeBlend_ParsesModeToMatchingRBlendMode(string mode, string expectedModeName)
        {
            var document = BuildFrom($"""<filter id="f"><feBlend mode="{mode}"/></filter>""");

            var blend = Assert.IsType<FeBlend>(Assert.Single(document.Filters["f"].Primitives));
            Assert.Equal(expectedModeName, blend.Mode.ToString());
        }

        [Fact]
        public void NonFePrimitiveChildren_AreSkipped_NotRejected()
        {
            var document = BuildFrom("""<filter id="f"><title>My filter</title><feFlood/></filter>""");

            Assert.True(document.Filters.ContainsKey("f"));
            Assert.Single(document.Filters["f"].Primitives);
        }
    }
}
