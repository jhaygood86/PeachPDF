using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Coverage for SVG <c>&lt;text&gt;</c>'s <c>text-transform</c> support (issue #533), applied to
    /// each text fragment in <see cref="SvgTreeBuilder.BuildTextRun"/> before whitespace collapsing.
    /// </summary>
    public class SvgTextTransformTests
    {
        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        private static TestRecordingGraphics Render(string body)
        {
            var markup = $$"""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100">
                  {{body}}
                </svg>
                """;
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new RRect(0, 0, 200, 100));
            return g;
        }

        [Fact]
        public void Uppercase_TransformsWholeFragment()
        {
            var g = Render("""<text x="10" y="50" font-size="20" text-transform="uppercase">hello world</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("HELLO WORLD", draw.Text);
        }

        [Fact]
        public void Lowercase_TransformsWholeFragment()
        {
            var g = Render("""<text x="10" y="50" font-size="20" text-transform="lowercase">HELLO</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("hello", draw.Text);
        }

        [Fact]
        public void Capitalize_CapitalizesEachWord()
        {
            var g = Render("""<text x="10" y="50" font-size="20" text-transform="capitalize">hello world</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("Hello World", draw.Text);
        }

        [Fact]
        public void Capitalize_CrossesTspanBoundary_MidWord()
        {
            // "wor" + "ld" is one word split across a <tspan> - capitalize must still only capitalize
            // the very first letter of the whole word ("W"), not re-trigger at the tspan boundary, and
            // "ld" (mid-word) must NOT capitalize. The whitespace between "hello" and the tspan collapses
            // onto the *start* of the tspan's own fragment (SVG's cross-run collapsing model), not the
            // end of "hello"'s - an implementation detail of where the space lands, not of capitalize
            // itself.
            var g = Render("""<text x="10" y="50" font-size="20" text-transform="capitalize">hello <tspan>wor</tspan>ld</text>""");

            Assert.Equal(3, g.DrawStringCalls.Count);
            Assert.Equal("Hello", g.DrawStringCalls[0].Text);
            Assert.Equal(" Wor", g.DrawStringCalls[1].Text);
            Assert.Equal("ld", g.DrawStringCalls[2].Text);
        }

        [Fact]
        public void Capitalize_WordStartsWithNonLetter_CapitalizesFirstActualLetter()
        {
            var g = Render("""<text x="10" y="50" font-size="20" text-transform="capitalize">123abc</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("123Abc", draw.Text);
        }

        [Fact]
        public void Uppercase_AppliesToTrefContent_UsingTheTrefsOwnResolvedContext()
        {
            // Regression: a <tref>'s text-transform must resolve using the <tref>'s own immediate
            // parent's resolved context (here, the tspan's own text-transform: uppercase), not one
            // level further up (the tspan's own *inherited* value, none here) - otherwise an ancestor's
            // text-transform silently fails to apply to the referenced text.
            var g = Render("""
                <defs><text id="src">hello world</text></defs>
                <text x="10" y="50" font-size="20"><tspan text-transform="uppercase"><tref href="#src"/></tspan></text>
                """);

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("HELLO WORLD", draw.Text);
        }

        [Fact]
        public void FullWidth_TransformsAsciiToFullwidthForms()
        {
            var g = Render("""<text x="10" y="50" font-size="20" text-transform="full-width">AB</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("ＡＢ", draw.Text);
        }

        [Fact]
        public void None_LeavesTextUnchanged()
        {
            var g = Render("""<text x="10" y="50" font-size="20" text-transform="none">Hello</text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("Hello", draw.Text);
        }

        [Fact]
        public void TspanCanResetToNone_OverridingInheritedTransform()
        {
            var g = Render("""<text x="10" y="50" font-size="20" text-transform="uppercase">AB<tspan text-transform="none">cd</tspan></text>""");

            Assert.Equal(2, g.DrawStringCalls.Count);
            Assert.Equal("AB", g.DrawStringCalls[0].Text);
            Assert.Equal("cd", g.DrawStringCalls[1].Text);
        }

        [Fact]
        public void InteractsWithWhitespaceCollapsing_TransformAppliesBeforeCollapse()
        {
            // Leading/trailing/doubled whitespace still collapses per SVG 1.1 §10.15, on top of the
            // (length-preserving) text-transform applied first.
            var g = Render("""<text x="10" y="50" font-size="20" text-transform="uppercase">  hello   world  </text>""");

            var draw = Assert.Single(g.DrawStringCalls);
            Assert.Equal("HELLO WORLD", draw.Text);
        }
    }
}
