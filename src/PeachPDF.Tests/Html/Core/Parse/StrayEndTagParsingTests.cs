using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace PeachPDF.Tests.Html.Core.Parse
{
    /// <summary>
    /// How the tree builder answers an end tag whose element is not open —
    /// <see href="https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inbody">HTML
    /// §13.2.6.4.7 "in body"</see>.
    ///
    /// <para>
    /// Almost every such end tag is a parse error the algorithm simply ignores. <c>&lt;/p&gt;</c> is the
    /// exception it answers by <b>generating an element</b>: "if the stack of open elements does not have
    /// a <c>p</c> element in button scope, then this is a parse error; insert an HTML element for a
    /// <c>p</c> start tag token with no attributes. Close a <c>p</c> element." The generated paragraph is
    /// empty and immediately closed, so nothing moves — what changes is the <b>element count</b>, and so
    /// what the sibling combinators match.
    /// </para>
    ///
    /// <para>
    /// Every expectation here is the child-element list headless Chrome reports for the same markup.
    /// </para>
    /// </summary>
    public class StrayEndTagParsingTests
    {
        [Fact]
        public void MatchedParagraph_IsOneElement()
        {
            Assert.Equal(["p"], ChildElementsOf("<div id='t'><p>a</p></div>"));
        }

        [Fact]
        public void StrayParagraphEndTag_GeneratesAnEmptyParagraph()
        {
            // Nothing opened a <p> at all, so this end tag is unmatched from the start.
            Assert.Equal(["p"], ChildElementsOf("<div id='t'></p></div>"));
        }

        [Fact]
        public void StrayParagraphEndTag_AfterAMatchedOne_GeneratesAnotherParagraph()
        {
            // The first </p> closes the real paragraph; the second has nothing left to close.
            Assert.Equal(["p", "p"], ChildElementsOf("<div id='t'><p>a</p></p></div>"));
        }

        [Fact]
        public void TwoStrayParagraphEndTags_GenerateTwoParagraphs()
        {
            // Each is answered on its own - the first generates and closes a paragraph, which leaves the
            // second just as unmatched as the first was.
            Assert.Equal(["p", "p"], ChildElementsOf("<div id='t'></p></p></div>"));
        }

        [Fact]
        public void ParagraphClosedByATable_ThenAStrayEndTag_IsAcid2sShape()
        {
            // <table> implies the </p>, so the author's own </p> after it is stray and generates a third
            // element between the table and the next paragraph. This is what makes Acid2's
            // ".picture p + table + p" match a paragraph that is not "p.bad" - see
            // Acid2RegressionTests.FixedPositionMargin_ShiftsSecondParagraphBelowFirstsBlackBar.
            Assert.Equal(["p", "table", "p", "p"],
                ChildElementsOf("<div id='t'><p><table><tr><td></td></tr></table></p><p class='bad'>x</p></div>"));
        }

        [Theory]
        // The control, and the reason this is a special case rather than a general rule: every OTHER
        // unmatched end tag is dropped, generating nothing and leaving the insertion point where it is.
        // Answering them the way </p> is answered would invent elements throughout a malformed document.
        [InlineData("<div id='t'></span></div>")]
        [InlineData("<div id='t'></em></div>")]
        [InlineData("<div id='t'></blockquote></div>")]
        public void OtherStrayEndTags_GenerateNothing(string html)
        {
            Assert.Empty(ChildElementsOf(html));
        }

        [Fact]
        public void GeneratedParagraph_CarriesNoAttributes()
        {
            // "insert an HTML element for a p start tag token with NO ATTRIBUTES" - it must not inherit
            // anything from the end tag's own source text or from the paragraph it stands in for.
            var generated = Assert.Single(ParagraphsOf("<div id='t'></p></div>"));

            Assert.Null(generated.HtmlTag!.TryGetAttribute("class"));
            Assert.Null(generated.HtmlTag.TryGetAttribute("id"));
        }

        [Fact]
        public void GeneratedParagraph_IsEmpty_AndDoesNotSwallowWhatFollowsIt()
        {
            // It is closed as soon as it is inserted, so the content after it stays a SIBLING. Getting
            // this wrong would reparent the rest of the document inside the generated element.
            Assert.Equal(["p", "span"], ChildElementsOf("<div id='t'></p><span>after</span></div>"));
        }

        [Fact]
        public void ParagraphOutsideButtonScope_IsNotClosedByTheEndTag()
        {
            const string html =
                "<div id='t'><p><button id='button'></p><span>after</span></button></div>";

            Assert.Equal(["p"], ChildElementsOf(html));
            Assert.Equal(["p", "span"], ChildElementsOf(html, "button"));
        }

        [Fact]
        public void ParagraphEndTag_InSelect_IsIgnored()
        {
            Assert.Equal(["option"], ChildElementsOf(
                "<select id='t'></p><option>x</option></select>"));
        }

        [Fact]
        public void ParagraphEndTag_InTable_IsFosterParentedBeforeTheTable()
        {
            const string html =
                "<div id='t'><table id='table'></p><tbody><tr><td>x</td></tr></tbody></table></div>";

            Assert.Equal(["p", "table"], ChildElementsOf(html));
            Assert.DoesNotContain("p", ChildElementsOf(html, "table"));
        }

        /// <summary>
        /// The tag names of <c>#t</c>'s child elements, in order — the box-tree equivalent of
        /// <c>element.children</c>, which is what the Chrome runs these are checked against report.
        /// </summary>
        private static List<string> ChildElementsOf(string bodyHtml, string id = "t") =>
            FindById(HtmlParser.ParseDocument($"<!DOCTYPE html><html><body>{bodyHtml}</body></html>"), id)!
                .Boxes
                .Where(b => b.HtmlTag is not null)
                .Select(b => b.HtmlTag!.Name.ToLowerInvariant())
                .ToList();

        private static List<CssBox> ParagraphsOf(string bodyHtml) =>
            FindById(HtmlParser.ParseDocument($"<!DOCTYPE html><html><body>{bodyHtml}</body></html>"), "t")!
                .Boxes
                .Where(b => b.HtmlTag?.Name.Equals("p", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

        private static CssBox? FindById(CssBox box, string id)
        {
            if (box.HtmlTag?.TryGetAttribute("id") == id) return box;

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }

            return null;
        }
    }
}
