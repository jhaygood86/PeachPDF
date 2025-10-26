using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Integration tests for content() function that verify PeachPDF's content property
    /// supports the CSS GCPM-3 spec for the content() function.
    /// </summary>
    public class ContentFunctionIntegrationTests
    {
        [Fact]
        public async Task ContentFunction_WithTextMode_ExtractsElementText()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1::before { content: content(text); }
 </style>
</head>
<body>
    <h1>Dynamic Heading</h1>
</body>
</html>";

            var (h1Box, rootBox) = await BuildAndFindBox(html, "h1");
            var beforeBox = h1Box.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);

            Assert.NotNull(beforeBox);
            Assert.Equal("Dynamic Heading", beforeBox.Text);
        }

        [Fact]
        public async Task ContentFunction_WithDefaultMode_ExtractsElementText()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1::before { content: content(); }
    </style>
</head>
<body>
    <h1>Test Content</h1>
</body>
</html>";

            var (h1Box, rootBox) = await BuildAndFindBox(html, "h1");
            var beforeBox = h1Box.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);

            Assert.NotNull(beforeBox);
            Assert.Equal("Test Content", beforeBox.Text);
        }

        [Fact]
        public async Task ContentFunction_WithBeforeMode_ExtractsBeforePseudoElementText()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
 <style>
 h1::before { content: ""Chapter ""; }
 h1::after { content: content(before); }
    </style>
</head>
<body>
    <h1>One</h1>
</body>
</html>";

            var (h1Box, rootBox) = await BuildAndFindBox(html, "h1");
            var afterBox = h1Box.Boxes.FirstOrDefault(b => b.IsAfterPseudoElement);

            Assert.NotNull(afterBox);
            Assert.Equal("Chapter ", afterBox.Text);
        }

        [Fact]
        public async Task ContentFunction_WithAfterMode_ExtractsAfterPseudoElementText()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
      h1::after { content: "".txt""; }
        .filename::before { content: ""File: "" content(text) content(after); }
  </style>
</head>
<body>
    <h1 class=""filename"">document</h1>
</body>
</html>";

            var (h1Box, rootBox) = await BuildAndFindBox(html, "h1");
            var beforeBox = h1Box.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);

            Assert.NotNull(beforeBox);
            Assert.Equal("File: document.txt", beforeBox.Text);
        }

        [Fact]
        public async Task ContentFunction_WithFirstLetterMode_ExtractsFirstLetter()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
p::before { content: content(first-letter); }
    </style>
</head>
<body>
    <p>Hello World</p>
</body>
</html>";

            var (pBox, rootBox) = await BuildAndFindBox(html, "p");
            var beforeBox = pBox.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);

            Assert.NotNull(beforeBox);
            Assert.Equal("H", beforeBox.Text);
        }

        [Fact]
        public async Task ContentFunction_CombinedWithString_ConcatenatesCorrectly()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
h1::before { content: ""Chapter "" content(text); }
    </style>
</head>
<body>
    <h1>One</h1>
</body>
</html>";

            var (h1Box, rootBox) = await BuildAndFindBox(html, "h1");
            var beforeBox = h1Box.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);

            Assert.NotNull(beforeBox);
            Assert.Equal("Chapter One", beforeBox.Text);
        }

        [Fact]
        public async Task ContentFunction_CombinedWithCounter_WorksCorrectly()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
 body { counter-reset: chapter; }
  h1 { counter-increment: chapter; }
        h1::before { content: counter(chapter) "". "" content(text); }
    </style>
</head>
<body>
    <h1>Introduction</h1>
    <h1>Background</h1>
</body>
</html>";

            var (h1Boxes, rootBox) = await BuildAndFindAllBoxes(html, "h1");
            var lastH1 = h1Boxes.Last();
            var beforeBox = lastH1.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);

            Assert.NotNull(beforeBox);
            Assert.Equal("2. Background", beforeBox.Text);
        }

        [Fact]
        public async Task ContentFunction_InStringSet_ExtractsElementText()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1 { string-set: chapter-title content(text); }
  </style>
</head>
<body>
    <h1>Chapter One</h1>
</body>
</html>";

            var (h1Box, rootBox) = await BuildAndFindBox(html, "h1");

            CssNamedStringEngine.ApplyStringSet(h1Box);

            Assert.True(h1Box.NamedStrings.ContainsKey("chapter-title"));
            Assert.Equal("Chapter One", h1Box.NamedStrings["chapter-title"].Value);
        }

        [Fact]
        public async Task ContentFunction_MultipleContentValues_ConcatenatesAll()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1::before { content: ""[""; }
   h1::after { content: ""]""; }
        h1 { string-set: heading content(before) content(text) content(after); }
    </style>
</head>
<body>
  <h1>Title</h1>
</body>
</html>";

            var (h1Box, rootBox) = await BuildAndFindBox(html, "h1");

            CssNamedStringEngine.ApplyStringSet(h1Box);

            Assert.True(h1Box.NamedStrings.ContainsKey("heading"));
            Assert.Equal("[Title]", h1Box.NamedStrings["heading"].Value);
        }

        [Fact]
        public async Task ContentFunction_WithNestedElements_ExtractsAllText()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
     h1::before { content: content(text); }
  </style>
</head>
<body>
    <h1>Hello <em>World</em></h1>
</body>
</html>";

            var (h1Box, rootBox) = await BuildAndFindBox(html, "h1");
            var beforeBox = h1Box.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);

            Assert.NotNull(beforeBox);
            Assert.Contains("Hello", beforeBox.Text);
            Assert.Contains("World", beforeBox.Text);
        }

        [Fact]
        public async Task ContentFunction_WithEmptyElement_ReturnsEmptyString()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1::before { content: content(text); }
    </style>
</head>
<body>
    <h1></h1>
</body>
</html>";

            var (h1Box, rootBox) = await BuildAndFindBox(html, "h1");
            var beforeBox = h1Box.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);

            // Content function should produce empty string for empty elements
            Assert.NotNull(beforeBox);
            Assert.True(string.IsNullOrEmpty(beforeBox.Text));
        }

        [Fact]
        public async Task ContentFunction_InAfterPseudoElement_AccessesParentText()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
    p::after { content: "" (Length: "" content(text) "")""; }
    </style>
</head>
<body>
    <p>Test</p>
</body>
</html>";

            var (pBox, rootBox) = await BuildAndFindBox(html, "p");
            var afterBox = pBox.Boxes.FirstOrDefault(b => b.IsAfterPseudoElement);

            Assert.NotNull(afterBox);
            Assert.Equal(" (Length: Test)", afterBox.Text);
        }

        /// <summary>
        /// Builds CssBox tree and finds a single box by tag name.
        /// </summary>
        private async Task<(CssBox box, CssBox root)> BuildAndFindBox(string html, string tagName)
        {
            var (root, _) = await BuildCssBoxTree(html);
            var box = DomUtils.GetBoxByTagName(root, tagName);
            Assert.NotNull(box);
            return (box!, root);
        }

        /// <summary>
        /// Builds CssBox tree and finds all boxes matching any of the tag names.
        /// </summary>
        private async Task<(CssBox[] boxes, CssBox root)> BuildAndFindAllBoxes(string html, params string[] tagNames)
        {
            var (root, _) = await BuildCssBoxTree(html);
            var boxes = tagNames.Select(tag => FindAllBoxesByTagName(root, tag)).SelectMany(b => b).ToArray();
            Assert.NotEmpty(boxes);
            return (boxes, root);
        }

        private CssBox[] FindAllBoxesByTagName(CssBox root, string tagName)
        {
            var results = new System.Collections.Generic.List<CssBox>();
            FindAllBoxesByTagNameRecursive(root, tagName, results);
            return results.ToArray();
        }

        private void FindAllBoxesByTagNameRecursive(CssBox box, string tagName, System.Collections.Generic.List<CssBox> results)
        {
            if (box.HtmlTag?.Name.Equals(tagName, System.StringComparison.OrdinalIgnoreCase) == true)
            {
                results.Add(box);
            }
            foreach (var child in box.Boxes)
            {
                FindAllBoxesByTagNameRecursive(child, tagName, results);
            }
        }

        /// <summary>
        /// Builds the complete CssBox tree from HTML.
        /// </summary>
        private async Task<(CssBox root, HtmlContainerInt container)> BuildCssBoxTree(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);

            await container.SetHtml(html, null);

            // Perform layout
            var size = new XSize(595, 842); // A4 size
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }
    }
}
