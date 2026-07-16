using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Tests.Html.Core.Handlers
{
    public class StructureTagMapperTests
    {
        [Fact]
        public void Classify_ProxyBox_ReturnsNone()
        {
            // A CssProxyBox delegates its own paint to its source box's own Paint() call, which
            // independently reaches Classify for the same underlying element - the proxy itself
            // must be transparent, or the source's struct element would get double-wrapped.
            var source = new CssBox(null, new HtmlTag("thead", false)) { PdfTagType = "thead" };
            var proxy = new CssProxyBox(null, source);

            var classification = StructureTagMapper.Classify(proxy);

            Assert.Equal(StructureTagKind.None, classification.Kind);
        }

        [Fact]
        public void Classify_UnrecognizedTagTypeValue_FallsBackToAuto()
        {
            // Shouldn't be reachable via real CSS (the property's Converter only accepts the fixed
            // keyword set), but Classify defends against it anyway rather than emitting a bogus /S.
            var box = new CssBox(null, new HtmlTag("div", false)) { PdfTagType = "not-a-real-value", Display = CssConstants.Block };

            var classification = StructureTagMapper.Classify(box);

            Assert.Equal(StructureTagKind.Grouping, classification.Kind);
            Assert.Equal("Div", classification.StructureType);
        }

        [Fact]
        public void Classify_ArtifactTagType_ReturnsArtifact()
        {
            var box = new CssBox(null, new HtmlTag("hr", true)) { PdfTagType = "artifact" };

            var classification = StructureTagMapper.Classify(box);

            Assert.Equal(StructureTagKind.Artifact, classification.Kind);
        }

        [Theory]
        [InlineData("part", "Part")]
        [InlineData("art", "Art")]
        [InlineData("sect", "Sect")]
        [InlineData("div", "Div")]
        [InlineData("index", "Index")]
        [InlineData("blockquote", "BlockQuote")]
        [InlineData("caption", "Caption")]
        [InlineData("toc", "TOC")]
        [InlineData("toci", "TOCI")]
        [InlineData("p", "P")]
        [InlineData("h1", "H1")]
        [InlineData("h2", "H2")]
        [InlineData("h3", "H3")]
        [InlineData("h4", "H4")]
        [InlineData("h5", "H5")]
        [InlineData("h6", "H6")]
        [InlineData("l", "L")]
        [InlineData("li", "LI")]
        [InlineData("lbl", "Lbl")]
        [InlineData("lbody", "LBody")]
        [InlineData("dl", "DL")]
        [InlineData("dl-div", "DL-Div")]
        [InlineData("dt", "DT")]
        [InlineData("dd", "DD")]
        [InlineData("span", "Span")]
        [InlineData("quote", "Quote")]
        [InlineData("table", "Table")]
        [InlineData("tr", "TR")]
        [InlineData("th", "TH")]
        [InlineData("td", "TD")]
        [InlineData("thead", "THead")]
        [InlineData("tbody", "TBody")]
        [InlineData("tfoot", "TFoot")]
        [InlineData("bibentry", "BibEntry")]
        [InlineData("code", "Code")]
        [InlineData("figure", "Figure")]
        [InlineData("formula", "Formula")]
        [InlineData("note", "Note")]
        [InlineData("reference", "Reference")]
        [InlineData("link", "Link")]
        public void Classify_ExplicitTagTypeValue_ResolvesToCanonicalStructureType(string cssValue, string expectedStructureType)
        {
            var box = new CssBox(null, new HtmlTag("div", false)) { PdfTagType = cssValue };

            var classification = StructureTagMapper.Classify(box);

            Assert.Equal(expectedStructureType, classification.StructureType);
        }

        [Fact]
        public void Classify_BareAnchorWithNoHrefIdOrName_FallsBackToLinkViaIsClickable()
        {
            // a[href] { -peachpdf-pdf-tag-type: Link } in the default stylesheet only fires for
            // an <a> with an href - a bare <a> (no href, no id, no name) never matches that rule,
            // stays at PdfTagType "auto", and falls through to the auto-resolution's IsClickable
            // check instead (CssBox.IsClickable is true for exactly this shape of <a>).
            var box = new CssBox(null, new HtmlTag("a", false)) { PdfTagType = CssConstants.Auto };

            var classification = StructureTagMapper.Classify(box);

            Assert.Equal(StructureTagKind.Grouping, classification.Kind);
            Assert.Equal("Link", classification.StructureType);
        }

        [Theory]
        [InlineData(CssConstants.Inline, "Span")]
        [InlineData(CssConstants.Block, "Div")]
        public void Classify_UncommonTagWithNoDefaultStylesheetRule_FallsBackToDisplayBasedType(string display, string expectedStructureType)
        {
            // <cite> has no default-stylesheet -peachpdf-pdf-tag-type rule, so it stays "auto" and
            // falls through to the plain block/inline fallback.
            var box = new CssBox(null, new HtmlTag("cite", false)) { PdfTagType = CssConstants.Auto, Display = display };

            var classification = StructureTagMapper.Classify(box);

            Assert.Equal(expectedStructureType, classification.StructureType);
        }

        [Theory]
        [InlineData(CssConstants.TableRow, "TR")]
        [InlineData(CssConstants.TableCell, "TD")]
        [InlineData(CssConstants.TableHeaderGroup, "THead")]
        [InlineData(CssConstants.TableRowGroup, "TBody")]
        [InlineData(CssConstants.TableFooterGroup, "TFoot")]
        public void Classify_AnonymousTableModelBox_MapsDisplayToTableStructureType(string display, string expectedStructureType)
        {
            // Anonymous boxes (HtmlTag == null) synthesized by CorrectAnonymousTables to complete
            // a table model have no source element for -peachpdf-pdf-tag-type to be set on - this
            // is the one place table substructure tagging is still Display-driven, not CSS-driven.
            var box = new CssBox(null, null) { PdfTagType = CssConstants.Auto, Display = display };

            var classification = StructureTagMapper.Classify(box);

            Assert.Equal(StructureTagKind.Grouping, classification.Kind);
            Assert.Equal(expectedStructureType, classification.StructureType);
        }

        [Fact]
        public void Classify_AnonymousNonTableBoxWithNoWords_IsFullyTransparent()
        {
            var box = new CssBox(null, null) { PdfTagType = CssConstants.Auto, Display = CssConstants.Block };

            var classification = StructureTagMapper.Classify(box);

            Assert.Equal(StructureTagKind.None, classification.Kind);
        }
    }
}
