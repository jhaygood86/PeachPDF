using PeachDrawing.Core;
using PeachPDF.Adapters;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>SVG text reading the <c>font</c>, <c>font-variant</c> and <c>text-decoration</c> shorthands from <c>style=""</c>, a presentation attribute and a stylesheet.</summary>
    public class SvgTextShorthandTests
    {
        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        private static SvgTextElement BuildText(string svgBody)
        {
            var markup = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">{svgBody}</svg>""";
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            return (SvgTextElement)document.Children[0];
        }

        [Fact]
        public void FontShorthand_InStyle_SetsSizeWeightAndStyle()
        {
            var text = BuildText("""<text x="10" y="50" style="font: italic bold 30px sans-serif">Hi</text>""");

            Assert.Equal(30, text.Font!.Size, 1);
        }

        [Fact]
        public void FontShorthand_AsAttribute_SetsSize()
        {
            var text = BuildText("""<text x="10" y="50" font="40px sans-serif">Hi</text>""");

            Assert.Equal(40, text.Font!.Size, 1);
        }

        [Fact]
        public void FontShorthand_ResetsUnlistedLonghands()
        {
            // font-variant-caps is reset to normal by the shorthand even though the presentation attribute asked for small-caps.
            var text = BuildText("""<text x="10" y="50" font-variant-caps="all-small-caps" style="font: 20px sans-serif">Hi</text>""");

            Assert.Equal(PeachDrawing.Text.Shaping.CapsMode.None, text.ShapingFeatures.Caps);
        }

        [Fact]
        public void FontVariantShorthand_InStyle_SetsLigatures()
        {
            var text = BuildText("""<text x="10" y="50" style="font-variant: no-common-ligatures">Hi</text>""");

            Assert.False((text.ShapingFeatures.Ligatures & PeachDrawing.Text.Shaping.LigatureSet.Common) != 0);
        }

        [Fact]
        public void TextDecorationShorthand_AsAttributeAndStyle()
        {
            var attr = BuildText("""<text x="10" y="50" text-decoration="underline">Hi</text>""");
            Assert.Equal("underline", attr.TextDecorationLine);

            var style = BuildText("""<text x="10" y="50" style="text-decoration: line-through dotted">Hi</text>""");
            Assert.Equal("line-through", style.TextDecorationLine);
            Assert.Equal("dotted", style.TextDecorationStyle);
        }

        [Fact]
        public void TextDecorationShorthand_InStylesheet()
        {
            var markup = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100"><style>text { text-decoration: overline wavy }</style><text x="10" y="50">Hi</text></svg>""";
            var root = XDocument.Parse(markup).Root!;
            var css = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root));
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, css, "print", null), Adapter);
            var text = (SvgTextElement)document.Children[0];

            Assert.Equal("overline", text.TextDecorationLine);
            Assert.Equal("wavy", text.TextDecorationStyle);
        }
    }
}
