using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using System;
using System.Text;
using Xunit;

namespace PeachBrackets.Engine.Tests.Html.Core.Parse
{
    /// <summary>
    /// The document-level insertion modes of the HTML tree construction algorithm (initial through "after after body"), the "in template"
    /// mode, the remaining "in body" rules and the breakout from foreign content.
    /// </summary>
    public class DocumentTreeConstructionTests
    {
        private static CssBox Parse(string html) => HtmlParser.ParseDocument(html, document: true);

        private static string Document(string html)
        {
            var sb = new StringBuilder();
            foreach (var child in Parse(html).Boxes)
                Serialize(child, sb);

            return sb.ToString();
        }

        private static void Serialize(CssBox box, StringBuilder sb)
        {
            if (box.HtmlTag is not { } tag)
            {
                sb.Append(box.Text);
                return;
            }

            sb.Append('<').Append(tag.Name.ToLowerInvariant()).Append('>');
            if (tag.IsSingle) return;

            foreach (var child in box.Boxes)
                Serialize(child, sb);

            sb.Append("</").Append(tag.Name.ToLowerInvariant()).Append('>');
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

        [Theory]
        // Whatever the source leaves out of the skeleton is supplied.
        [InlineData("", "<html><head></head><body></body></html>")]
        [InlineData("x", "<html><head></head><body>x</body></html>")]
        [InlineData("  <p>a", "<html><head></head><body><p>a</p></body></html>")]
        [InlineData("<title>t</title><p>a", "<html><head><title>t</title></head><body><p>a</p></body></html>")]
        [InlineData("<html><head><title>t</title></head><body><p>a</p></body></html>", "<html><head><title>t</title></head><body><p>a</p></body></html>")]
        [InlineData("<!DOCTYPE html><html><body>a", "<html><head></head><body>a</body></html>")]
        [InlineData("<body>a", "<html><head></head><body>a</body></html>")]
        [InlineData("<head><meta><p>a", "<html><head><meta></head><body><p>a</p></body></html>")]
        // Head content after the head goes into it; in the body it stays where it is.
        [InlineData("<head></head><meta><p>a", "<html><head><meta></head><body><p>a</p></body></html>")]
        [InlineData("<p>a<meta>b", "<html><head></head><body><p>a<meta>b</p></body></html>")]
        // Content after the end of the body goes into the body.
        [InlineData("<p>a</p></body>b", "<html><head></head><body><p>a</p>b</body></html>")]
        [InlineData("<body>a</body></html>b<p>c", "<html><head></head><body>ab<p>c</p></body></html>")]
        // A second html or body is not an element.
        [InlineData("<html><body><html><body>a", "<html><head></head><body>a</body></html>")]
        // An end tag for the body inside a table cell is ignored.
        [InlineData("<table><tr><td></body>x", "<html><head></head><body><table><tbody><tr><td>x</td></tr></tbody></table></body></html>")]
        public void TheSkeletonOfADocument_IsSupplied(string html, string expected) => Assert.Equal(expected, Document(html));

        [Fact]
        public void AnHtmlOrBodyStartTagThatIsNotTheFirst_AddsItsMissingAttributes()
        {
            var root = Parse("<html a=1><body c=1><html a=2 b=2><body c=2 d=2>x");
            var html = Find(root, "html")!.HtmlTag!;
            var body = Find(root, "body")!.HtmlTag!;

            Assert.Equal("1", html.TryGetAttribute("a"));
            Assert.Equal("2", html.TryGetAttribute("b"));
            Assert.Equal("1", body.TryGetAttribute("c"));
            Assert.Equal("2", body.TryGetAttribute("d"));
        }

        [Theory]
        [InlineData("<template><tr><td>x</td></tr></template>", "<html><head><template><tr><td>x</td></tr></template></head><body></body></html>")]
        [InlineData("<p><template><tr><td>x</template>", "<html><head></head><body><p><template><tr><td>x</td></tr></template></p></body></html>")]
        [InlineData("<template><div>a</div></template>b", "<html><head><template><div>a</div></template></head><body>b</body></html>")]
        [InlineData("<template><p>a</template><p>b", "<html><head><template><p>a</p></template></head><body><p>b</p></body></html>")]
        [InlineData("<template><td>a<td>b</template>", "<html><head><template><td>a</td><td>b</td></template></head><body></body></html>")]
        [InlineData("<template><template><i>a</template>b</template>c",
            "<html><head><template><template><i>a</i></template>b</template></head><body>c</body></html>")]
        public void TemplateContents_AreParsedByTheirFirstTag(string html, string expected) => Assert.Equal(expected, Document(html));

        [Theory]
        [InlineData("<pre>\nx</pre>", "<html><head></head><body><pre>x</pre></body></html>")]
        [InlineData("<pre>\n\nx</pre>", "<html><head></head><body><pre>\nx</pre></body></html>")]
        [InlineData("<listing>\nx</listing>", "<html><head></head><body><listing>x</listing></body></html>")]
        [InlineData("<image src=a>", "<html><head></head><body><img></body></html>")]
        [InlineData("<ruby>a<rb>b<rt>c<rb>d</ruby>", "<html><head></head><body><ruby>a<rb>b</rb><rt>c</rt><rb>d</rb></ruby></body></html>")]
        [InlineData("<svg><p>x</p>", "<html><head></head><body><svg></svg><p>x</p></body></html>")]
        [InlineData("<svg><g><span>x", "<html><head></head><body><svg><g></g></svg><span>x</span></body></html>")]
        [InlineData("<svg><g>x</g></svg>y", "<html><head></head><body><svg><g>x</g></svg>y</body></html>")]
        [InlineData("<svg><font color=red>x", "<html><head></head><body><svg></svg><font>x</font></body></html>")]
        public void TheRemainingInBodyRules_AreFollowed(string html, string expected) => Assert.Equal(expected, Document(html));

        [Fact]
        public void SvgElementNames_GetTheirMixedCase()
        {
            var clip = Find(Parse("<svg><clippath id=c></clippath><lineargradient></lineargradient></svg>"), "clippath");

            Assert.Equal("clipPath", clip!.HtmlTag!.Name);
            Assert.Equal("linearGradient", Find(Parse("<svg><lineargradient></lineargradient></svg>"), "lineargradient")!.HtmlTag!.Name);
        }

        [Fact]
        public void AFragment_GetsNoSkeleton()
        {
            var root = HtmlParser.ParseDocument("<p>a</p>");

            Assert.Equal("p", Assert.Single(root.Boxes).HtmlTag!.Name);
        }
    }
}
