using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using PeachPDF;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Html.Core.Parse
{
    /// <summary>
    /// An XHTML document is parsed with an XML parser, as a browser does for one served as <c>application/xhtml+xml</c>.
    /// </summary>
    public class XhtmlParserTests
    {
        private const string Ns = "http://www.w3.org/1999/xhtml";

        private static string Page(string body, string head = "", string doctype = "") =>
            $"{doctype}<html xmlns=\"{Ns}\"><head><title>t</title>{head}</head><body style=\"margin:0\">{body}</body></html>";

        private static CssBox Parse(string xhtml) => XhtmlParser.ParseDocument(xhtml, out _);

        private static IEnumerable<CssBox> All(CssBox box)
        {
            yield return box;
            foreach (var child in box.Boxes)
                foreach (var d in All(child))
                    yield return d;
        }

        private static CssBox Find(CssBox root, string name) => All(root).First(b => b.HtmlTag?.Name == name);

        private static string TextOf(CssBox box) => string.Concat(All(box).Select(b => b.Text));

        private static Task<(CssBox Root, HtmlContainerInt Container)> LayoutXmlAsync(string xhtml) =>
            LayoutAsync(xhtml, margin: 0, configureContainer: c => c.DocumentSyntax = DocumentSyntax.Xml);

        [Fact]
        public void TheDocumentIsNeverInQuirksMode()
        {
            XhtmlParser.ParseDocument(Page("x"), out var mode);

            Assert.Equal(DocumentMode.NoQuirks, mode);
        }

        [Fact]
        public void TheTreeIsTheOneTheMarkupSaysAndNothingIsMadeUp()
        {
            var root = Parse($"<html xmlns=\"{Ns}\"><body><p>a</p></body></html>");

            var html = Assert.Single(root.Boxes);
            Assert.Equal("html", html.HtmlTag!.Name);
            // No head is made up, as the HTML parser would.
            Assert.Equal(["body"], html.Boxes.Select(b => b.HtmlTag!.Name));
        }

        [Fact]
        public void NoTableSectionOrParagraphIsImplied()
        {
            var root = Parse(Page("<table><tr><td>x</td></tr></table>"));

            var table = Find(root, "table");
            Assert.Equal(["tr"], table.Boxes.Select(b => b.HtmlTag!.Name));
        }

        [Fact]
        public void ElementNamesInTheXhtmlNamespaceAreLowercase()
        {
            var root = Parse($"<HTML xmlns=\"{Ns}\"><BODY/></HTML>".Replace("HTML", "html"));

            Assert.NotNull(Find(root, "html"));
        }

        [Fact]
        public void WellFormednessIsCaseSensitive()
        {
            // <p></P> is an error in XML, where the HTML parser would take it as a paragraph.
            Assert.Throws<HtmlRenderException>(() => Parse(Page("<p>x</P>")));
            Assert.Throws<HtmlRenderException>(() => Parse(Page("<P>x</p>")));
        }

        [Fact]
        public void AnElementNameInTheXhtmlNamespaceIsTakenAsItsLowercaseForm()
        {
            // The engine matches HTML names ignoring case, so <DIV> is a <div> here (a browser makes it an unknown element): a known deviation.
            var root = Parse(Page("<DIV id=\"a\">x</DIV>"));

            Assert.Equal("div", Find(root, "div").HtmlTag!.Name);
        }

        [Fact]
        public void AnEmptyElementIsEmptyAndNothingElseIs()
        {
            var root = Parse(Page("<div id=\"a\"/><p id=\"b\">x</p><br/><div id=\"c\"></div>"));

            Assert.Empty(All(root).Single(b => b.HtmlTag?.TryGetAttribute("id") == "a").Boxes);
            Assert.Equal("x", TextOf(All(root).Single(b => b.HtmlTag?.TryGetAttribute("id") == "b")));
            // A <div/> does not open an element the rest of the page falls into: the paragraph is its sibling.
            var body = Find(root, "body");
            Assert.Equal(["div", "p", "br", "div"], body.Boxes.Select(b => b.HtmlTag!.Name));
        }

        [Fact]
        public void ACdataSectionIsText()
        {
            var root = Parse(Page("<p>a</p>", head: "<style>/* <![CDATA[ */ p > b { color: red } /* ]]> */</style>"));

            Assert.Contains("p > b { color: red }", TextOf(Find(root, "style")));
        }

        [Fact]
        public void AdjacentTextAndCdataAreOneRun()
        {
            var root = Parse(Page("<p>a<![CDATA[b<c]]>d&amp;e</p>"));

            var paragraph = Find(root, "p");
            var run = Assert.Single(paragraph.Boxes);
            Assert.Equal("ab<cd&e", run.Text);
        }

        [Theory]
        [InlineData("&nbsp;", "\u00A0")]
        [InlineData("&copy;", "\u00A9")]
        [InlineData("&eacute;", "\u00E9")]
        [InlineData("&mdash;", "\u2014")]
        [InlineData("&hellip;", "\u2026")]
        [InlineData("&euro;", "\u20AC")]
        [InlineData("&Alpha;", "\u0391")]
        [InlineData("&alpha;", "\u03B1")]
        [InlineData("&lt;&gt;&amp;&quot;&apos;", "<>&\"'")]
        [InlineData("&#169;&#xA9;", "\u00A9\u00A9")]
        public void NamedEntitiesOfXhtmlAreKnownWithAnXhtmlDoctype(string entity, string expected)
        {
            const string doctype =
                "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Strict//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd\">";

            var root = Parse(Page($"<p>{entity}</p>", doctype: doctype));

            Assert.Equal(expected, TextOf(Find(root, "p")));
        }

        [Theory]
        [InlineData("<!DOCTYPE html>")]
        [InlineData("<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\">")]
        [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!-- c -->\n<!DOCTYPE html>")]
        [InlineData("<!DOCTYPE html SYSTEM \"about:legacy-compat\">")]
        public void NamedEntitiesAreKnownWithAnyDoctype(string doctype)
        {
            var root = Parse(Page("<p>&nbsp;&copy;</p>", doctype: doctype));

            Assert.Equal("\u00A0\u00A9", TextOf(Find(root, "p")));
        }

        [Fact]
        public void AnEntityIsUndefinedWithNoDoctype()
        {
            var error = Assert.Throws<HtmlRenderException>(() => Parse(Page("<p>&nbsp;</p>")));

            Assert.Equal(HtmlRenderErrorType.HtmlParsing, error.RenderErrorType);
            Assert.IsType<System.Xml.XmlException>(error.InnerException);
        }

        [Fact]
        public void AnUnknownEntityIsAnErrorEvenWithADoctype()
        {
            Assert.Throws<HtmlRenderException>(() => Parse(Page("<p>&nosuchentity;</p>", doctype: "<!DOCTYPE html>")));
        }

        [Fact]
        public void AnEntityOfTheInternalSubsetIsExpanded()
        {
            var root = Parse(Page("<p>&who;</p>", doctype: "<!DOCTYPE html [ <!ENTITY who \"World\"> ]>"));

            Assert.Equal("World", TextOf(Find(root, "p")));
        }

        [Fact]
        public void AnEntityExpansionBombIsRefused()
        {
            var doctype = "<!DOCTYPE html [ <!ENTITY a \"aaaaaaaaaa\"> <!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\"> "
                + "<!ENTITY c \"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;\"> <!ENTITY d \"&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;\"> "
                + "<!ENTITY e \"&d;&d;&d;&d;&d;&d;&d;&d;&d;&d;\"> <!ENTITY f \"&e;&e;&e;&e;&e;&e;&e;&e;&e;&e;\"> "
                + "<!ENTITY g \"&f;&f;&f;&f;&f;&f;&f;&f;&f;&f;\"> <!ENTITY h \"&g;&g;&g;&g;&g;&g;&g;&g;&g;&g;\"> ]>";

            Assert.Throws<HtmlRenderException>(() => Parse(Page("<p>&h;</p>", doctype: doctype)));
        }

        [Theory]
        [InlineData("<!DOCTYPE html SYSTEM \"file:///C:/Windows/win.ini\">")]
        [InlineData("<!DOCTYPE html SYSTEM \"http://127.0.0.1:1/x.dtd\">")]
        [InlineData("<!DOCTYPE html [ <!ENTITY secret SYSTEM \"file:///C:/Windows/win.ini\"> ]>")]
        public void AnExternalReferenceNeverReachesTheFileSystemOrTheNetwork(string doctype)
        {
            // The resolver answers every external reference from memory, so this returns at once instead of reading or connecting.
            var root = Parse(Page("<p>x</p>", doctype: doctype));

            Assert.Equal("x", TextOf(Find(root, "p")));
        }

        [Fact]
        public void TheResolverServesTheEntityListForADtdAndNothingForOtherReferences()
        {
            var resolver = new XhtmlEntityResolver();

            var dtd = resolver.ResolveUri(null, "xhtml1-strict.dtd");
            var other = resolver.ResolveUri(new Uri("file:///C:/secret/"), "passwd.txt");

            Assert.Equal(XhtmlEntityResolver.EntitiesUri, dtd.OriginalString);
            using var dtdStream = (System.IO.Stream)resolver.GetEntity(dtd, null, null)!;
            using var otherStream = (System.IO.Stream)resolver.GetEntity(other, null, null)!;
            Assert.True(dtdStream.Length > 1000);
            Assert.Equal(0, otherStream.Length);
        }

        [Fact]
        public void TheEmbeddedEntityListHoldsTheHtml4Set()
        {
            // The HTML 4 set is 252 names; amp, lt, gt and quot are predefined by XML.
            var declared = XhtmlEntityResolver.EntitiesDtd.Split('\n').Count(l => l.StartsWith("<!ENTITY ", StringComparison.Ordinal));

            Assert.Equal(248, declared);
        }

        [Fact]
        public void SvgAndMathKeepTheCaseOfTheirNamesAndTakeTheirNamespaces()
        {
            var root = Parse(Page(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"0 0 10 10\">"
                + "<clipPath id=\"c\"><rect width=\"1\" height=\"1\"/></clipPath><use xlink:href=\"#c\"/></svg>"
                + "<math xmlns=\"http://www.w3.org/1998/Math/MathML\"><mi>x</mi></math>"));

            Assert.IsType<CssBoxSvg>(Find(root, "svg"));
            Assert.NotNull(Find(root, "clipPath"));
            Assert.Equal("#c", Find(root, "use").HtmlTag!.TryGetAttribute("xlink:href"));
            Assert.Equal("0 0 10 10", Find(root, "svg").HtmlTag!.TryGetAttribute("viewBox"));
            Assert.IsType<CssBoxMath>(Find(root, "math"));
            Assert.Equal("x", Find(root, "mi").ForeignText);
        }

        [Fact]
        public void ANameThatOnlyLooksLikeAnHtmlElementInAnotherNamespaceIsAPlainBox()
        {
            var root = Parse(Page("<img xmlns=\"urn:other\" src=\"x.png\"/><x:input xmlns:x=\"urn:other\"/>"));

            Assert.Equal(2, All(root).Count(b => b.HtmlTag is { Name: "img" or "x:input" }));
            Assert.DoesNotContain(All(root), b => b is CssBoxImage or CssBoxFormField);
        }

        [Fact]
        public void TheLanguageIsReadFromXmlLangToo()
        {
            var root = Parse($"<html xmlns=\"{Ns}\" xml:lang=\"fr\"><body><p lang=\"de\" xml:lang=\"en\">x</p></body></html>");

            Assert.Equal("fr", Find(root, "html").HtmlTag!.TryGetAttribute("lang"));
            Assert.Equal("de", Find(root, "p").HtmlTag!.TryGetAttribute("lang"));
            Assert.Equal("en", Find(root, "p").HtmlTag!.TryGetAttribute("xml:lang"));
        }

        [Fact]
        public void AnHtmlWithNoNamespaceIsAccepted()
        {
            var root = Parse("<html><body><p>x</p></body></html>");

            Assert.Equal("x", TextOf(Find(root, "p")));
        }

        [Fact]
        public void ABomIsIgnored()
        {
            var root = Parse("\uFEFF" + Page("<p>x</p>"));

            Assert.Equal("x", TextOf(Find(root, "p")));
        }

        [Theory]
        [InlineData("<html xmlns=\"http://www.w3.org/1999/xhtml\"><body><p>unclosed</body></html>")]
        [InlineData("<html xmlns=\"http://www.w3.org/1999/xhtml\"><body><br></body></html>")]
        [InlineData("<html xmlns=\"http://www.w3.org/1999/xhtml\"><body a=b></body></html>")]
        [InlineData("<html xmlns=\"http://www.w3.org/1999/xhtml\"><body></body></html><html/>")]
        [InlineData("<!doctype html><html xmlns=\"http://www.w3.org/1999/xhtml\"/>")]
        [InlineData("")]
        [InlineData("not xml")]
        public void MarkupThatIsNotWellFormedIsAnError(string xhtml)
        {
            var error = Assert.Throws<HtmlRenderException>(() => Parse(xhtml));

            Assert.Equal(HtmlRenderErrorType.HtmlParsing, error.RenderErrorType);
        }

        [Fact]
        public void TheErrorSaysWhere()
        {
            var error = Assert.Throws<HtmlRenderException>(() => Parse("<html xmlns=\"" + Ns + "\">\n<body>\n<p></b></body></html>"));

            Assert.Contains("ine 3", error.Message);
        }

        [Theory]
        [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"/>")]
        [InlineData("<body xmlns=\"http://www.w3.org/1999/xhtml\"/>")]
        public void TheDocumentElementMustBeHtml(string xhtml)
        {
            var error = Assert.Throws<HtmlRenderException>(() => Parse(xhtml));

            Assert.Contains("<html>", error.Message);
        }

        [Fact]
        public void ADeepDocumentIsRefusedAtTheSameDepthAsHtml()
        {
            var deep = string.Concat(Enumerable.Repeat("<div>", HtmlParser.MaxElementDepth + 5));

            Assert.Throws<HtmlRenderException>(() => Parse(Page(deep)));
        }

        [Fact]
        public void TextOutsideTheDocumentElementMakesNoBox()
        {
            var root = Parse("<!-- c -->\n" + Page("x") + "\n<!-- d -->");

            Assert.Single(root.Boxes);
        }

        [Fact]
        public async Task TheTreeLaysOutLikeTheSameDocumentParsedAsHtml()
        {
            var body = "<h1>Title</h1><p>Some <b>bold</b> text &amp; more.</p><ul><li>one</li><li>two</li></ul><table><tbody><tr><td>c</td></tr></tbody></table>";
            var (xml, _) = await LayoutXmlAsync(Page(body, doctype: "<!DOCTYPE html>"));
            var (html, _) = await LayoutAsync("<!DOCTYPE html>" + Page(body), margin: 0);

            var xmlBoxes = All(xml).Where(b => b.HtmlTag is not null && b.HtmlTag.Name is not ("head" or "title")).ToList();
            var htmlBoxes = All(html).Where(b => b.HtmlTag is not null && b.HtmlTag.Name is not ("head" or "title")).ToList();

            Assert.Equal(htmlBoxes.Select(b => b.HtmlTag!.Name), xmlBoxes.Select(b => b.HtmlTag!.Name));
            for (var i = 0; i < xmlBoxes.Count; i++)
            {
                Assert.Equal(htmlBoxes[i].Location.Y, xmlBoxes[i].Location.Y, 0.01);
                Assert.Equal(htmlBoxes[i].ActualBottom, xmlBoxes[i].ActualBottom, 0.01);
                Assert.Equal(htmlBoxes[i].ActualRight, xmlBoxes[i].ActualRight, 0.01);
            }
        }

        [Fact]
        public void AnSvgWithNoNamespaceOfItsOwnKeepsTheCaseOfWhatIsInsideIt()
        {
            var root = Parse(Page(
                "<svg width=\"10\" height=\"10\" viewBox=\"0 0 1 1\"><defs><linearGradient id=\"g\"/></defs><clipPath id=\"c\"/></svg><div id=\"after\"/>"));

            Assert.IsType<CssBoxSvg>(Find(root, "svg"));
            Assert.NotNull(Find(root, "linearGradient"));
            Assert.NotNull(Find(root, "clipPath"));
            // Back outside the svg the names are HTML's again.
            Assert.Equal("div", Find(root, "div").HtmlTag!.Name);
        }

        [Fact]
        public void ForeignObjectTakesHtmlBackWithItsLowercaseNames()
        {
            var root = Parse(Page("<svg><foreignObject><DIV id=\"x\">y</DIV></foreignObject></svg>"));

            Assert.NotNull(Find(root, "foreignObject"));
            Assert.Equal("div", All(root).First(b => b.HtmlTag?.Name == "div").HtmlTag!.Name);
        }

        [Fact]
        public async Task APrettyPrintedDocumentLaysOutLikeTheSameDocumentParsedAsHtml()
        {
            // Real XHTML files have white space between every element. The HTML parser drops some of it (between html, head and body, in a table
            // row); the XML parser keeps all of it, and the result must still be the same page.
            var xhtml = $$"""
                <?xml version="1.0"?>
                <!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Strict//EN" "http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd">
                <html xmlns="{{Ns}}">
                  <head>
                    <title>t</title>
                    <style type="text/css">body { margin: 0 } td { border: 1px solid }</style>
                  </head>
                  <body>
                    <h1>Title</h1>
                    <ul>
                      <li>one</li>
                      <li>two</li>
                    </ul>
                    <table>
                      <tbody>
                        <tr>
                          <td>a</td>
                          <td>b</td>
                        </tr>
                      </tbody>
                    </table>
                    <p>end</p>
                  </body>
                </html>
                """;
            var (xml, _) = await LayoutXmlAsync(xhtml);
            var (html, _) = await LayoutAsync(xhtml.Replace("<?xml version=\"1.0\"?>\n", ""), margin: 0);

            var xmlBoxes = All(xml).Where(b => b.HtmlTag is not null && b.HtmlTag.Name is not ("head" or "title" or "style")).ToList();
            var htmlBoxes = All(html).Where(b => b.HtmlTag is not null && b.HtmlTag.Name is not ("head" or "title" or "style")).ToList();

            Assert.Equal(htmlBoxes.Select(b => b.HtmlTag!.Name), xmlBoxes.Select(b => b.HtmlTag!.Name));
            for (var i = 0; i < xmlBoxes.Count; i++)
            {
                Assert.Equal(htmlBoxes[i].Location.Y, xmlBoxes[i].Location.Y, 0.01);
                Assert.Equal(htmlBoxes[i].ActualBottom, xmlBoxes[i].ActualBottom, 0.01);
                Assert.Equal(htmlBoxes[i].Location.X, xmlBoxes[i].Location.X, 0.01);
                Assert.Equal(htmlBoxes[i].ActualRight, xmlBoxes[i].ActualRight, 0.01);
            }
        }

        [Fact]
        public async Task AnXmlDocumentWithNoDoctypeIsNotInQuirksMode()
        {
            // As HTML the same text is in quirks mode, where the first paragraph has no top margin.
            var xhtml = Page("<p id=\"p\">x</p>");
            var (xml, xmlContainer) = await LayoutXmlAsync(xhtml);
            var (html, htmlContainer) = await LayoutAsync(xhtml, margin: 0);

            Assert.Equal(DocumentMode.NoQuirks, xmlContainer.DocumentMode);
            Assert.Equal(DocumentMode.Quirks, htmlContainer.DocumentMode);
            Assert.True(FindById(xml, "p")!.Location.Y > 0);
            Assert.Equal(0.0, FindById(html, "p")!.Location.Y, 0.01);
        }

        [Fact]
        public async Task TheHtmlParserStillTreatsTheSameTextAsHtml()
        {
            // The syntax is chosen by the host, never from the text: an XML declaration does not turn a document into XML.
            var (root, container) = await LayoutAsync("<?xml version=\"1.0\"?>" + Page("<p id=\"p\">a<br>b</p>"), margin: 0);

            Assert.Equal(DocumentSyntax.Html, container.DocumentSyntax);
            Assert.NotNull(FindById(root, "p"));
        }

        [Fact]
        public async Task StylesAndInlineStylesApplyInAnXmlDocument()
        {
            var (root, _) = await LayoutXmlAsync(Page(
                "<div id=\"a\" class=\"c\" style=\"width:100px\">x</div>", head: "<style><![CDATA[ .c { height: 20px; background-color: rgb(1,2,3) } ]]></style>"));
            var box = FindById(root, "a")!;

            Assert.Equal(75.0, box.ActualBoxSizingWidth, 0.01);
            Assert.Equal(15.0, box.ActualBoxSizingHeight, 0.01);
            Assert.Equal("rgb(1, 2, 3)", box.BackgroundColor);
        }
    }
}
