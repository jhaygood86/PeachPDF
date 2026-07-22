using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Svg;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    public class SvgCssDomNodeTests
    {
        private static ICssDomNode XmlRoot(string markup)
        {
            var root = XDocument.Parse(markup).Root!;
            return new SvgXmlDomNode(root, root);
        }

        [Fact]
        public void XmlNode_ReportsCaseSensitiveOrdinalMatching()
        {
            var node = XmlRoot("""<svg xmlns="http://www.w3.org/2000/svg"/>""");
            Assert.Equal(System.StringComparison.Ordinal, node.NameComparison);
            Assert.Equal("svg", node.TagName);
            Assert.False(node.IsRoot);
        }

        [Fact]
        public void XmlNode_GetAttribute_IsCaseSensitive()
        {
            var svg = XmlRoot("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"/>""");
            Assert.Equal("0 0 10 10", svg.GetAttribute("viewBox"));
            Assert.Null(svg.GetAttribute("viewbox")); // wrong case does not resolve (XML is case-sensitive)
        }

        [Fact]
        public void XmlNode_GetAttribute_ResolvesXlinkHref()
        {
            var svg = XmlRoot("""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"><use xlink:href="#a"/></svg>""");
            var use = svg.Children.Single(c => c.TagName == "use");
            Assert.Equal("#a", use.GetAttribute("xlink:href"));
        }

        [Fact]
        public void XmlNode_ParentIsNullAtRoot_ButNavigatesChildren()
        {
            var svg = XmlRoot("""<svg xmlns="http://www.w3.org/2000/svg"><g><rect/></g></svg>""");
            Assert.Null(svg.Parent);
            var g = svg.Children.Single();
            Assert.Equal("g", g.TagName);
            Assert.Equal(svg, g.Parent);        // value-equality on the underlying element
            var rect = g.Children.Single();
            Assert.Equal("rect", rect.TagName);
            Assert.Equal(g, rect.Parent);
        }

        [Fact]
        public void XmlNode_Equality_KeysOffUnderlyingElement()
        {
            var svg = XmlRoot("""<svg xmlns="http://www.w3.org/2000/svg"><rect/></svg>""");
            // Two independently-created wrappers over the same element compare equal (matcher relies on this).
            var a = svg.Children.Single();
            var b = svg.Children.Single();
            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void MatchedDeclarations_FullSelectorEngine_CombinatorAndCaseSensitive()
        {
            var markup = """
                <svg xmlns="http://www.w3.org/2000/svg">
                  <style>g rect { fill: #00ff00; } RECT { stroke: #ff0000; }</style>
                  <g><rect class="target"/></g>
                </svg>
                """;
            var root = XDocument.Parse(markup).Root!;
            var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root));
            SvgCssStyling.CascadeCustomProperties(root, cssData, "print");

            var rectElement = root.Descendants().Single(e => e.Name.LocalName == "rect");
            var rectNode = new SvgXmlDomNode(rectElement, root);
            var matched = SvgCssStyling.GetMatchedDeclarations(rectNode, cssData, "print");

            Assert.NotNull(matched);
            // "g rect" combinator matched; the CSS-OM normalizes the hex color to rgb() form.
            Assert.Equal("rgb(0, 255, 0)", matched!["fill"]);
            Assert.False(matched.ContainsKey("stroke"));     // "RECT" did NOT match <rect> (case-sensitive)
        }

        [Theory]
        [InlineData("rect[data-x*=\"idd\"]", true)]   // substring
        [InlineData("rect[data-x^=\"hi\"]", true)]    // prefix
        [InlineData("rect[data-x$=\"en\"]", true)]    // suffix
        [InlineData("rect[data-x*=\"IDD\"]", false)]  // substring, wrong case (SVG is case-sensitive)
        public void MatchedDeclarations_SubstringAttributeSelectors(string selector, bool shouldMatch)
        {
            var markup = $$"""
                <svg xmlns="http://www.w3.org/2000/svg">
                  <style>{{selector}} { fill: #00ff00; }</style>
                  <rect data-x="hidden"/>
                </svg>
                """;
            var root = XDocument.Parse(markup).Root!;
            var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root));

            var rectElement = root.Descendants().Single(e => e.Name.LocalName == "rect");
            var matched = SvgCssStyling.GetMatchedDeclarations(new SvgXmlDomNode(rectElement, root), cssData, "print");

            Assert.Equal(shouldMatch, matched!.ContainsKey("fill"));
        }

        [Fact]
        public void CustomPropertyCascade_InheritsAndVarResolves()
        {
            var markup = """
                <svg xmlns="http://www.w3.org/2000/svg">
                  <style>:root { --c: #00ff00; } rect { fill: var(--c); }</style>
                  <g><rect/></g>
                </svg>
                """;
            var root = XDocument.Parse(markup).Root!;
            var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root));
            SvgCssStyling.CascadeCustomProperties(root, cssData, "print");

            var rectElement = root.Descendants().Single(e => e.Name.LocalName == "rect");
            var rectNode = new SvgXmlDomNode(rectElement, root);

            // --c inherited from :root down through <g> to <rect>.
            Assert.Equal("#00ff00", rectNode.CustomProperties!["--c"]);
            // and var(--c) resolves in the matched fill.
            var matched = SvgCssStyling.GetMatchedDeclarations(rectNode, cssData, "print");
            Assert.Equal("#00ff00", matched!["fill"]);
        }

        [Fact]
        public void CssVarResolver_ResolvesFallbackAndDetectsMissing()
        {
            var node = new StubNode(new Dictionary<string, string> { ["--a"] = "#0000ff" });
            Assert.Equal("#0000ff", CssVarResolver.Resolve(node, "var(--a)"));
            Assert.Equal("red", CssVarResolver.Resolve(node, "var(--missing, red)"));
            Assert.Null(CssVarResolver.Resolve(node, "var(--missing)")); // guaranteed-invalid
            Assert.Equal("10px", CssVarResolver.Resolve(node, "10px"));  // no var(): unchanged
        }

        private sealed class StubNode(Dictionary<string, string> customProperties) : ICssDomNode
        {
            public string? TagName => "rect";
            public string? GetAttribute(string name) => null;
            public System.StringComparison NameComparison => System.StringComparison.Ordinal;
            public ICssDomNode? Parent => null;
            public IReadOnlyList<ICssDomNode> Children => [];
            public bool IsRoot => false;
            public Dictionary<string, string>? CustomProperties { get; set; } = customProperties;
        }
    }
}
