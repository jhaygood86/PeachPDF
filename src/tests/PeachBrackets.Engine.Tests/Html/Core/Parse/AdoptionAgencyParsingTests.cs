using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using PeachPDF;
using System;
using System.Linq;
using System.Text;
using Xunit;

namespace PeachBrackets.Engine.Tests.Html.Core.Parse
{
    /// <summary>
    /// How the tree builder nests formatting elements and what it does with an end tag whose element is not the one open
    /// (<see href="https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inbody">HTML §13.2.6.4.7 "in body"</see>,
    /// the <see href="https://html.spec.whatwg.org/multipage/parsing.html#adoption-agency-algorithm">adoption agency algorithm</see> and
    /// <see href="https://html.spec.whatwg.org/multipage/parsing.html#reconstruct-the-active-formatting-elements">reconstructing the
    /// active formatting elements</see>).
    ///
    /// <para>Every expectation is the tree the HTML Standard's own examples and headless Chrome give for the same markup.</para>
    /// </summary>
    public class AdoptionAgencyParsingTests
    {
        [Theory]
        // The standard's own example of a formatting element ended while a block is open inside it.
        [InlineData("<b>1<p>2</b>3</p>", "<b>1</b><p><b>2</b>3</p>")]
        [InlineData("<b><i>x</b>y</i>", "<b><i>x</i></b><i>y</i>")]
        [InlineData("<a>x<p>y</a>z", "<a>x</a><p><a>y</a>z</p>")]
        [InlineData("<b>1<div>2<div>3</b>4</div>5</div>", "<b>1</b><div><b>2</b><div><b>3</b>4</div>5</div>")]
        // A second anchor ends the first.
        [InlineData("<a>1<a>2", "<a>1</a><a>2</a>")]
        // Nothing open inside the element: it is simply closed.
        [InlineData("<b>1</b>2", "<b>1</b>2")]
        [InlineData("<i><b>1</b>2</i>", "<i><b>1</b>2</i>")]
        // An end tag for a formatting element that is not open is ignored.
        [InlineData("1</b>2", "12")]
        public void MisnestedFormatting_IsAdoptedTheWayTheStandardSays(string html, string expected) => Assert.Equal(expected, Body(html));

        [Theory]
        // Formatting still open when a block ends is opened again in what follows.
        [InlineData("<p><b>x</p>y", "<p><b>x</b></p><b>y</b>")]
        [InlineData("<ul><li><b>a<li>b</ul>", "<ul><li><b>a</b></li><li><b>b</b></li></ul>")]
        [InlineData("<p><b>x<p>y", "<p><b>x</b></p><p><b>y</b></p>")]
        [InlineData("<div><i>a</div><div>b</div>", "<div><i>a</i></div><div><i>b</i></div>")]
        public void FormattingOpenWhenABlockEnds_IsReconstructedInTheNextContent(string html, string expected) => Assert.Equal(expected, Body(html));

        [Theory]
        // Formatting elements between the one ended and the block inside it are copied around the block; others are left behind.
        [InlineData("<b><i><div>1</b>2</i>3", "<b><i></i></b><i></i><div><i><b>1</b>2</i>3</div>")]
        [InlineData("<b><span><div>1</b>2", "<b><span></span></b><div><b>1</b>2</div>")]
        // The inner loop copies at most three of them.
        [InlineData("<b><i><u><s><small><div>x</b>y",
            "<b><i><u><s><small></small></s></u></i></b><u><s><small><div><b>x</b>y</div></small></s></u>")]
        // An end tag for an element the list remembers but that is no longer open just forgets it.
        [InlineData("<p><b>1</p></b>2", "<p><b>1</b></p>2")]
        // The oldest of four identical elements is not in the list, so its end tag is a plain close.
        [InlineData("<b><b><b><b>x</b></b></b></b>y", "<b><b><b><b>x</b></b></b></b>y")]
        // An element outside the scope of a table is not reached.
        [InlineData("<b>1<table></b><tr><td>x</td></tr></table>", "<b>1<table><tbody><tr><td>x</td></tr></tbody></table></b>")]
        // Cells and rows whose end tags are omitted.
        [InlineData("<table><tr><td>a<td>b<tr><td>c</table>", "<table><tbody><tr><td>a</td><td>b</td></tr><tr><td>c</td></tr></tbody></table>")]
        public void TheRestOfTheAdoptionAgencyAlgorithm_IsFollowed(string html, string expected) => Assert.Equal(expected, Body(html));

        [Fact]
        public void IdenticalReopenedElements_AreLimitedToThree()
        {
            // Noah's Ark clause: a fourth element with the same name and attributes drops the earliest from the list.
            var html = "<div><b>1</div><div><b>2</div><div><b>3</div><div><b>4</div><div>5</div>";

            Assert.Equal(
                "<div><b>1</b></div><div><b><b>2</b></b></div><div><b><b><b>3</b></b></b></div>"
                + "<div><b><b><b><b>4</b></b></b></b></div><div><b><b><b>5</b></b></b></div>",
                Body(html));
        }

        [Fact]
        public void AMarkerStopsReconstruction_AtTheEndOfATableCell()
        {
            Assert.Equal("<table><tbody><tr><td><b>x</b></td></tr></tbody></table>y", Body("<table><tr><td><b>x</td></tr></table>y"));
        }

        [Fact]
        public void ReopenedElements_AreCopiesWithTheirOwnAttributes()
        {
            var body = ParseBody("<b class='c'>1<p>2</b>3</p>");
            var first = body.Boxes[0];
            var reopened = body.Boxes[1].Boxes[0];

            Assert.Equal("b", first.HtmlTag!.Name);
            Assert.Equal("b", reopened.HtmlTag!.Name);
            Assert.NotSame(first, reopened);
            Assert.NotSame(first.HtmlTag, reopened.HtmlTag);
            Assert.NotSame(first.HtmlTag.Attributes, reopened.HtmlTag.Attributes);
            Assert.Equal("c", reopened.HtmlTag.TryGetAttribute("class"));

            reopened.HtmlTag.SetAttribute("class", "changed");
            Assert.Equal("c", first.HtmlTag.TryGetAttribute("class"));
        }

        [Theory]
        // A block end tag looks for its element only within scope: a table cell hides what is outside it.
        [InlineData("<div><table><tr><td></div>x</td></tr></table></div>", "<div><table><tbody><tr><td>x</td></tr></tbody></table></div>")]
        // A special element in the way ignores the end tag of one that is not special.
        [InlineData("<span><div>a</span>b</div>", "<span><div>ab</div></span>")]
        // Any other end tag closes the nearest open element of its name.
        [InlineData("<span><label>a</span>b", "<span><label>a</label></span>b")]
        [InlineData("<custom-element><label>a</custom-element>b", "<custom-element><label>a</label></custom-element>b")]
        public void EndTags_AreAnsweredWithinScope(string html, string expected) => Assert.Equal(expected, Body(html));

        [Theory]
        [InlineData("a</br>b", "a<br>b")]
        [InlineData("<h1>a<h2>b</h1>c", "<h1>a</h1><h2>b</h2>c")]
        [InlineData("<h3>a</h4>b</h3>", "<h3>a</h3>b")]
        [InlineData("<ol><li>a<li>b</ol>", "<ol><li>a</li><li>b</li></ol>")]
        [InlineData("<dl><dt>a<dd>b<dt>c</dl>", "<dl><dt>a</dt><dd>b</dd><dt>c</dt></dl>")]
        [InlineData("<p>a<button>b<button>c", "<p>a<button>b</button><button>c</button></p>")]
        [InlineData("<p><span><div>x</div></span>", "<p><span></span></p><div>x</div>")]
        [InlineData("<nobr>a<nobr>b", "<nobr>a</nobr><nobr>b</nobr>")]
        public void TheRestOfTheInBodyRules_AreFollowed(string html, string expected) => Assert.Equal(expected, Body(html));

        [Fact]
        public void FormattingTags_InsideSvg_BreakOutOfIt()
        {
            Assert.Equal("<svg></svg><b>x</b>", Body("<svg><b>x</b></svg>"));
        }

        [Fact]
        public void DeeplyMisnestedInput_TerminatesAndKeepsTheText()
        {
            var html = string.Concat(Enumerable.Repeat("<b><p>", 50)) + "x" + string.Concat(Enumerable.Repeat("</b>", 50)) + string.Concat(Enumerable.Repeat("</p>", 50));

            Assert.Contains("x", Body(html));
        }

        [Fact]
        public void ReopeningWithoutEnd_StaysWithinTheDepthLimit()
        {
            // Every <div> leaves one more distinct formatting element to reopen, which nests ever deeper.
            var html = string.Concat(Enumerable.Range(0, 600).Select(i => $"<div><b id='x{i}'>t</div>"));

            Assert.Throws<HtmlRenderException>(() => Body(html));
        }

        internal static string Body(string html)
        {
            var sb = new StringBuilder();
            foreach (var child in ParseBody(html).Boxes)
                Serialize(child, sb);

            return sb.ToString();
        }

        internal static CssBox ParseBody(string html)
        {
            var root = HtmlParser.ParseDocument($"<!DOCTYPE html><html><body>{html}</body></html>");
            return Find(root, "body")!;
        }

        private static CssBox? Find(CssBox box, string name)
        {
            if (box.HtmlTag?.Name.Equals(name, StringComparison.OrdinalIgnoreCase) == true) return box;

            foreach (var child in box.Boxes)
            {
                if (Find(child, name) is { } found) return found;
            }

            return null;
        }

        private static void Serialize(CssBox box, StringBuilder sb)
        {
            if (box.HtmlTag is not { } tag)
            {
                sb.Append(box.Text);
                return;
            }

            var name = tag.Name.ToLowerInvariant();
            sb.Append('<').Append(name).Append('>');
            if (tag.IsSingle) return;

            foreach (var child in box.Boxes)
                Serialize(child, sb);

            sb.Append("</").Append(name).Append('>');
        }
    }
}
