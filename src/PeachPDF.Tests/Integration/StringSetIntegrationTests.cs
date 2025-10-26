using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Integration tests for string-set that verify PeachPDF's CssBox tree construction
    /// and named string evaluation without requiring browser installation.
    /// </summary>
    public class StringSetIntegrationTests
    {
        [Fact]
        public async Task StringSet_BasicRendering_BuildsCssBoxWithNamedString()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1 { string-set: page-header ""Chapter 1""; }
    </style>
</head>
<body>
    <h1>First Heading</h1>
    <p>Some content here.</p>
</body>
</html>";

            var (h1Box, rootBox) = await BuildAndFindBox(html, "h1");

            // Verify h1 box has string-set property
            Assert.NotNull(h1Box);
            Assert.NotNull(h1Box.StringSet);
            Assert.Contains("page-header", h1Box.StringSet);

            // Apply string-set engine
            CssNamedStringEngine.ApplyStringSet(h1Box);

            // Verify named string was created
            Assert.True(h1Box.NamedStrings.ContainsKey("page-header"));
            Assert.Equal("Chapter 1", h1Box.NamedStrings["page-header"].Value);
        }

        [Fact]
        public async Task StringSet_WithCounter_BuildsNamedStringWithCounterValue()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
   body { counter-reset: page 1; }
    h1 { counter-increment: page; string-set: page-label ""Page "" counter(page); }
    </style>
</head>
<body>
    <h1>First Heading</h1><p>Content</p>
    <h1>Second Heading</h1><p>More content</p>
</body>
</html>";

            var (h1Boxes, rootBox) = await BuildAndFindAllBoxes(html, "h1");
            var lastH1 = h1Boxes.Last();

            // Apply string-set engine
            CssNamedStringEngine.ApplyStringSet(lastH1);

            // Verify named string contains counter value
            // Counter starts at 1, first h1 increments to 2, second h1 increments to 3
            Assert.True(lastH1.NamedStrings.ContainsKey("page-label"));
            Assert.Equal("Page 3", lastH1.NamedStrings["page-label"].Value);
        }

        [Fact]
        public async Task StringSet_WithContentText_ExtractsElementText()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1 { string-set: heading-text content(text); }
    </style>
</head>
<body>
    <h1>Dynamic Heading</h1>
    <p>Some content here.</p>
</body>
</html>";

            var (h1Box, rootBox) = await BuildAndFindBox(html, "h1");

            CssNamedStringEngine.ApplyStringSet(h1Box);

            Assert.True(h1Box.NamedStrings.ContainsKey("heading-text"));
            Assert.Equal("Dynamic Heading", h1Box.NamedStrings["heading-text"].Value);
        }

        [Fact]
        public async Task StringSet_WithAttr_ExtractsAttributeValue()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
      h1 { string-set: doc-title attr(data-title); }
    </style>
</head>
<body>
    <h1 data-title=""Custom Title"">Heading</h1>
    <p>Some content here.</p>
</body>
</html>";

            var (h1Box, rootBox) = await BuildAndFindBox(html, "h1");

            CssNamedStringEngine.ApplyStringSet(h1Box);

            Assert.True(h1Box.NamedStrings.ContainsKey("doc-title"));
            Assert.Equal("Custom Title", h1Box.NamedStrings["doc-title"].Value);
        }

        [Fact]
        public async Task StringSet_MultiplePairs_CreatesMultipleNamedStrings()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1 { string-set: header-text content(text), footer-text ""Page Footer""; }
    </style>
</head>
<body>
    <h1>Main Heading</h1>
    <p>Content goes here.</p>
</body>
</html>";

            var (h1Box, rootBox) = await BuildAndFindBox(html, "h1");

            CssNamedStringEngine.ApplyStringSet(h1Box);

            Assert.Equal(2, h1Box.NamedStrings.Count);
            Assert.Equal("Main Heading", h1Box.NamedStrings["header-text"].Value);
            Assert.Equal("Page Footer", h1Box.NamedStrings["footer-text"].Value);
        }

        [Fact]
        public async Task StringSet_WithNone_DoesNotSetString()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        h1 { string-set: page-header ""Initial""; }
        h2 { string-set: none; }
  </style>
</head>
<body>
    <h1>First Heading</h1><p>Content</p>
    <h2>Second Heading</h2><p>More content</p>
</body>
</html>";

            var (allBoxes, rootBox) = await BuildAndFindAllBoxes(html, "h1", "h2");
            var h1 = allBoxes.First(b => b.HtmlTag?.Name == "h1");
            var h2 = allBoxes.First(b => b.HtmlTag?.Name == "h2");

            CssNamedStringEngine.ApplyStringSet(h1);
            CssNamedStringEngine.ApplyStringSet(h2);

            // h1 should have named string
            Assert.True(h1.NamedStrings.ContainsKey("page-header"));
            Assert.Equal("Initial", h1.NamedStrings["page-header"].Value);

            // h2 with string-set:none should not modify named strings
            Assert.Equal("none", h2.StringSet);
        }

        [Fact]
        public async Task StringSet_InvalidSyntax_HandlesGracefully()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
  <style>
        h1 { string-set: header; /* Invalid: missing content-list */ }
    </style>
</head>
<body>
    <h1>Heading</h1>
    <p>Content</p>
</body>
</html>";

            var (h1Box, rootBox) = await BuildAndFindBox(html, "h1");

            // Should not throw
            CssNamedStringEngine.ApplyStringSet(h1Box);

            // Invalid syntax should result in no named strings
            Assert.Empty(h1Box.NamedStrings);
        }
        [Fact]
        public async Task StringSet_CssBoxTreeStructure_IsCorrect()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
      h1 { string-set: chapter ""Chapter "" content(text); }
   h2 { string-set: section ""Section "" content(text); }
    </style>
</head>
<body>
    <h1>One</h1><p>First paragraph.</p>
    <h2>Subsection A</h2><p>Second paragraph.</p>
</body>
</html>";

            var (rootBox, _) = await BuildCssBoxTree(html);

            // Verify box tree structure
            Assert.NotNull(rootBox);
            var bodyBox = DomUtils.GetBoxByTagName(rootBox, "body");
            Assert.NotNull(bodyBox);

            // Find all h1 and h2 boxes
            var h1Box = DomUtils.GetBoxByTagName(bodyBox, "h1");
            var h2Box = DomUtils.GetBoxByTagName(bodyBox, "h2");

            Assert.NotNull(h1Box);
            Assert.NotNull(h2Box);
            Assert.Contains("chapter", h1Box.StringSet);
            Assert.Contains("section", h2Box.StringSet);
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
