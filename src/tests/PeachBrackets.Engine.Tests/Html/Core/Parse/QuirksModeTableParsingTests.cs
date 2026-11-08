using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using System;
using Xunit;

namespace PeachBrackets.Engine.Tests.Html.Core.Parse
{
    /// <summary>
    /// A <c>table</c> start tag closes an open <c>p</c> only when the document is not in quirks mode (HTML Standard 13.2.6.4.7); quirks mode is
    /// decided by the DOCTYPE (13.2.6.4.1).
    /// </summary>
    public class QuirksModeTableParsingTests
    {
        private static CssBox? Find(CssBox box, string name)
        {
            if (box.HtmlTag?.Name.Equals(name, StringComparison.OrdinalIgnoreCase) == true) return box;
            foreach (var child in box.Boxes)
                if (Find(child, name) is { } found) return found;

            return null;
        }

        private static bool TableIsInParagraph(string html)
        {
            var table = Find(HtmlParser.ParseDocument(html, document: true), "table")!;
            for (var p = table.ParentBox; p is not null; p = p.ParentBox)
                if (p.HtmlTag?.Name.Equals("p", StringComparison.OrdinalIgnoreCase) == true) return true;

            return false;
        }

        private const string Body = "<p>a<table><tr><td>x</td></tr></table></p>";

        [Theory]
        [InlineData("<!DOCTYPE html><html><body>" + Body, false)]
        [InlineData("<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.01//EN\" \"http://www.w3.org/TR/html4/strict.dtd\"><body>" + Body, false)]
        [InlineData("<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.01 Transitional//EN\"><body>" + Body, true)]
        [InlineData("<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.01 Transitional//EN\" \"http://www.w3.org/TR/html4/loose.dtd\"><body>" + Body, false)]
        [InlineData("<!DOCTYPE HTML PUBLIC \"-//IETF//DTD HTML 2.0//EN\"><body>" + Body, true)]
        [InlineData("<!DOCTYPE foo><body>" + Body, true)]
        [InlineData("<html><body>" + Body, true)]
        [InlineData("<!-- c --> \n<html><body>" + Body, true)]
        [InlineData("<!-- c --> \n<!DOCTYPE html><body>" + Body, false)]
        public void TableStaysInTheParagraphOnlyInQuirksMode(string html, bool inParagraph) =>
            Assert.Equal(inParagraph, TableIsInParagraph(html));

        [Fact]
        public void AFragmentIsNeverInQuirksMode()
        {
            var table = Find(HtmlParser.ParseDocument(Body, document: false), "table")!;

            Assert.False(string.Equals(table.ParentBox?.HtmlTag?.Name, "p", StringComparison.OrdinalIgnoreCase));
        }
    }
}
