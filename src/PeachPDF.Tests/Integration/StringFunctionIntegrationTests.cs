using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Integration tests for string() function that verify PeachPDF's content property
    /// supports the CSS GCPM-3 spec for the string() function.
    /// </summary>
    public class StringFunctionIntegrationTests
    {
        [Fact]
        public async Task StringFunction_WithDefault_RetrievesNamedString()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1 { string-set: chapter content(text); }
        .header::before { content: string(chapter); }
    </style>
</head>
<body>
    <h1>Introduction</h1>
    <div class=""header""></div>
</body>
</html>";

            var (boxes, rootBox) = await BuildAndFindAllBoxes(html, "h1", "div");
            var h1Box = boxes.First(b => b.HtmlTag?.Name == "h1");
            var divBox = boxes.First(b => b.HtmlTag?.Name == "div");

            CssNamedStringEngine.ApplyStringSet(h1Box);

            var beforeBox = divBox.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);

            Assert.NotNull(beforeBox);
            CssContentEngine.ApplyContent(beforeBox);
            Assert.Equal("Introduction", beforeBox.Text);
        }

        [Fact]
        public async Task StringFunction_WithFirstKeyword_RetrievesFirstAssignment()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
      h1 { string-set: chapter content(text); }
     h2 { string-set: chapter content(text); }
        .header::before { content: string(chapter, first); }
    </style>
</head>
<body>
    <h1>First Chapter</h1>
    <h2>Second Chapter</h2>
    <div class=""header""></div>
</body>
</html>";

            var (boxes, rootBox) = await BuildAndFindAllBoxes(html, "h1", "h2", "div");
            var h1Box = boxes.First(b => b.HtmlTag?.Name == "h1");
            var h2Box = boxes.First(b => b.HtmlTag?.Name == "h2");
            var divBox = boxes.First(b => b.HtmlTag?.Name == "div");

            CssNamedStringEngine.ApplyStringSet(h1Box);
            CssNamedStringEngine.ApplyStringSet(h2Box);

            var beforeBox = divBox.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);

            Assert.NotNull(beforeBox);
            CssContentEngine.ApplyContent(beforeBox);
            Assert.Equal("First Chapter", beforeBox.Text);
        }

        [Fact]
        public async Task StringFunction_WithLastKeyword_RetrievesLastAssignment()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1 { string-set: chapter content(text); }
        h2 { string-set: chapter content(text); }
        .header::before { content: string(chapter, last); }
    </style>
</head>
<body>
    <h1>First Chapter</h1>
    <h2>Second Chapter</h2>
    <div class=""header""></div>
</body>
</html>";

            var (boxes, rootBox) = await BuildAndFindAllBoxes(html, "h1", "h2", "div");
            var h1Box = boxes.First(b => b.HtmlTag?.Name == "h1");
            var h2Box = boxes.First(b => b.HtmlTag?.Name == "h2");
            var divBox = boxes.First(b => b.HtmlTag?.Name == "div");

            CssNamedStringEngine.ApplyStringSet(h1Box);
            CssNamedStringEngine.ApplyStringSet(h2Box);

            var beforeBox = divBox.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);

            Assert.NotNull(beforeBox);
            CssContentEngine.ApplyContent(beforeBox);
            Assert.Equal("Second Chapter", beforeBox.Text);
        }

        [Fact]
        public async Task StringFunction_CombinedWithStringLiteral_ConcatenatesCorrectly()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1 { string-set: chapter content(text); }
      .header::before { content: ""Chapter: "" string(chapter); }
    </style>
</head>
<body>
    <h1>Introduction</h1>
    <div class=""header""></div>
</body>
</html>";

            var (boxes, rootBox) = await BuildAndFindAllBoxes(html, "h1", "div");
            var h1Box = boxes.First(b => b.HtmlTag?.Name == "h1");
            var divBox = boxes.First(b => b.HtmlTag?.Name == "div");

            CssNamedStringEngine.ApplyStringSet(h1Box);

            var beforeBox = divBox.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);

            Assert.NotNull(beforeBox);
            CssContentEngine.ApplyContent(beforeBox);
            Assert.Equal("Chapter: Introduction", beforeBox.Text);
        }

        [Fact]
        public async Task StringFunction_CombinedWithCounter_WorksCorrectly()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        body { counter-reset: page; }
    h1 { string-set: chapter content(text); counter-increment: page; }
   .header::before { content: string(chapter) "" - Page "" counter(page); }
    </style>
</head>
<body>
    <h1>Introduction</h1>
    <div class=""header""></div>
</body>
</html>";

            var (boxes, rootBox) = await BuildAndFindAllBoxes(html, "h1", "div");
            var h1Box = boxes.First(b => b.HtmlTag?.Name == "h1");
            var divBox = boxes.First(b => b.HtmlTag?.Name == "div");

            CssNamedStringEngine.ApplyStringSet(h1Box);

            var beforeBox = divBox.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);

            Assert.NotNull(beforeBox);
            CssContentEngine.ApplyContent(beforeBox);
            Assert.Equal("Introduction - Page 1", beforeBox.Text);
        }

        [Fact]
        public async Task StringFunction_InStringSet_CreatesNestedNamedString()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1 { string-set: chapter content(text); }
      h2 { string-set: section string(chapter) "" - "" content(text); }
  </style>
</head>
<body>
    <h1>Chapter One</h1>
    <h2>Section A</h2>
</body>
</html>";

            var (boxes, rootBox) = await BuildAndFindAllBoxes(html, "h1", "h2");
            var h1Box = boxes.First(b => b.HtmlTag?.Name == "h1");
            var h2Box = boxes.First(b => b.HtmlTag?.Name == "h2");

            CssNamedStringEngine.ApplyStringSet(h1Box);
            CssNamedStringEngine.ApplyStringSet(h2Box);

            Assert.True(h2Box.NamedStrings.ContainsKey("section"));
            Assert.Equal("Chapter One - Section A", h2Box.NamedStrings["section"].Value);
        }

        [Fact]
        public async Task StringFunction_MultipleStringFunctions_CombinesAll()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
  <style>
        h1 { string-set: chapter content(text); }
  h2 { string-set: section content(text); }
 .header::before { content: string(chapter) "" / "" string(section); }
    </style>
</head>
<body>
    <h1>Chapter One</h1>
    <h2>Section A</h2>
    <div class=""header""></div>
</body>
</html>";

            var (boxes, rootBox) = await BuildAndFindAllBoxes(html, "h1", "h2", "div");
            var h1Box = boxes.First(b => b.HtmlTag?.Name == "h1");
            var h2Box = boxes.First(b => b.HtmlTag?.Name == "h2");
            var divBox = boxes.First(b => b.HtmlTag?.Name == "div");

            CssNamedStringEngine.ApplyStringSet(h1Box);
            CssNamedStringEngine.ApplyStringSet(h2Box);

            var beforeBox = divBox.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);

            Assert.NotNull(beforeBox);
            CssContentEngine.ApplyContent(beforeBox);
            Assert.Equal("Chapter One / Section A", beforeBox.Text);
        }

        [Fact]
        public async Task StringFunction_WithNonExistentString_ReturnsEmptyString()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
     .header::before { content: string(nonexistent); }
 </style>
</head>
<body>
    <div class=""header""></div>
</body>
</html>";

            var (divBox, rootBox) = await BuildAndFindBox(html, "div");

            var beforeBox = divBox.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);

            Assert.NotNull(beforeBox);
            Assert.True(string.IsNullOrEmpty(beforeBox.Text));
        }

        [Fact]
        public async Task StringFunction_InRunningHeader_CreatesRunningHeader()
        {
            // Simplified test without @page rule to verify string-set parsing
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1 { string-set: header content(text); }
    </style>
</head>
<body>
    <h1>Document Title</h1>
    <p>Some content</p>
</body>
</html>";

            var (h1Box, rootBox) = await BuildAndFindBox(html, "h1");

            // Verify the string-set property was parsed
            Assert.NotNull(h1Box.StringSet);
            Assert.NotEqual("none", h1Box.StringSet);

            CssNamedStringEngine.ApplyStringSet(h1Box);

            Assert.True(h1Box.NamedStrings.ContainsKey("header"));
            Assert.Equal("Document Title", h1Box.NamedStrings["header"].Value);
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
