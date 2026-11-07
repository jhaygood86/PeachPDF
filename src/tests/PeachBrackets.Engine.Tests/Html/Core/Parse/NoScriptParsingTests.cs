using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using System;
using System.Linq;
using Xunit;

namespace PeachBrackets.Engine.Tests.Html.Core.Parse
{
    /// <summary>
    /// The content of a <c>&lt;noscript&gt;</c> is parsed as markup into the element. Text at its top level (the whitespace around a tag, as on
    /// real pages) once made that parse start over on the same text with the same root, without end: a stack overflow that ended the process.
    /// </summary>
    public class NoScriptParsingTests
    {
        [Fact]
        public void WhitespaceAroundATag_ParsesTheTagAndEnds()
        {
            var noscript = NoScriptOf("<noscript>" + (char)10 + (char)9 + "<img id='i' src='x.gif'>" + (char)10 + "</noscript>");

            Assert.Contains(noscript.Boxes, b => b.HtmlTag?.Name.Equals("img", StringComparison.OrdinalIgnoreCase) == true);
        }

        [Fact]
        public void PlainText_ParsesAndEnds()
        {
            var noscript = NoScriptOf("<noscript>Please enable JavaScript</noscript>");

            Assert.Contains("Please enable JavaScript", string.Concat(Descendants(noscript).Select(b => b.Text?.ToString())));
        }

        [Fact]
        public void TextBetweenTags_ParsesAndEnds()
        {
            var noscript = NoScriptOf("<noscript><b>a</b> and <i>b</i></noscript>");

            var names = Descendants(noscript).Select(b => b.HtmlTag?.Name.ToLowerInvariant()).Where(n => n is not null).ToList();
            Assert.Contains("b", names);
            Assert.Contains("i", names);
        }

        private static CssBox NoScriptOf(string noscriptHtml)
        {
            var root = HtmlParser.ParseDocument($"<!DOCTYPE html><html><body><div id='t'>{noscriptHtml}</div></body></html>", document: true);
            return Descendants(root).First(b => b.HtmlTag?.Name.Equals("noscript", StringComparison.OrdinalIgnoreCase) == true);
        }

        private static System.Collections.Generic.IEnumerable<CssBox> Descendants(CssBox box)
        {
            foreach (var child in box.Boxes)
            {
                yield return child;
                foreach (var inner in Descendants(child))
                    yield return inner;
            }
        }
    }
}
