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
        public async Task ContentFunction_ExtractsBeforeContentContainingStyledCounter()
        {
            // The extracted ::before content itself contains a styled counter - exercises the
            // counter(name, <style>) path inside content()'s pseudo-element extraction (issue #128).
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
      li::before { content: counter(list-item, decimal-leading-zero); }
      li::after { content: content(before); }
    </style>
</head>
<body>
    <ol><li>first</li><li>second</li></ol>
</body>
</html>";

            var (liBox, _) = await BuildAndFindBox(html, "li");
            var afterLi = liBox.Boxes.First(b => b.IsAfterPseudoElement);

            Assert.Equal("01", afterLi.Text);
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

        [Fact]
        public async Task CountersFunction_JoinsEveryValueInTheScopeChain()
        {
            // The classic nested-list numbering counters() exists for: the inner item shows its own
            // ancestry, outermost value first, not just its own value.
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        ol { counter-reset: item; list-style: none; }
        li { counter-increment: item; }
        li::before { content: counters(item, ""."") "" ""; }
    </style>
</head>
<body>
    <ol><li>One<ol><li id=""inner"">Nested</li></ol></li></ol>
</body>
</html>";

            var (root, _) = await BuildCssBoxTree(html);
            var inner = FindById(root, "inner");
            Assert.NotNull(inner);

            var before = inner!.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);
            Assert.NotNull(before);
            Assert.Equal("1.1 ", before!.Text);
        }

        [Fact]
        public async Task CountersFunction_HonoursItsCounterStyleArgument()
        {
            // The third argument used to be dropped entirely by the one implementation that existed
            // (reachable only from string-set); both callers now share one that reads it.
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        ol { counter-reset: item; list-style: none; }
        li { counter-increment: item; }
        li::before { content: counters(item, ""."", upper-roman); }
    </style>
</head>
<body>
    <ol><li>One</li><li id=""second"">Two</li></ol>
</body>
</html>";

            var (root, _) = await BuildCssBoxTree(html);
            var second = FindById(root, "second");
            Assert.NotNull(second);

            var before = second!.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);
            Assert.NotNull(before);
            Assert.Equal("II", before!.Text);
        }

        [Fact]
        public async Task CountersFunction_ForACounterThatWasNeverSet_IsZero()
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        p::before { content: counters(nothing, "".""); }
    </style>
</head>
<body>
    <p>Text</p>
</body>
</html>";

            var (pBox, _) = await BuildAndFindBox(html, "p");
            var before = pBox.Boxes.FirstOrDefault(b => b.IsBeforePseudoElement);

            Assert.NotNull(before);
            Assert.Equal("0", before!.Text);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase)) return box;

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }

            return null;
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
