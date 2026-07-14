using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Integration tests for the CSS <c>text-transform</c> property. Assertions read the transformed
    /// text off <c>CssRectWord.Text</c> (post layout), since that's the single point both measurement
    /// and painting read from - it's the transform actually taking effect, not just the cascaded
    /// keyword landing on the box.
    /// </summary>
    public class TextTransformTests
    {
        [Fact]
        public async Task Uppercase_TransformsWordText()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="text-transform: uppercase">hello world</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("HELLO", FindFirstWord(el!)!.Text);
        }

        [Fact]
        public async Task Lowercase_TransformsWordText()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="text-transform: lowercase">HELLO WORLD</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("hello", FindFirstWord(el!)!.Text);
        }

        [Fact]
        public async Task Capitalize_UppercasesFirstLetterOfEachWord()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="text-transform: capitalize">hello world</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            var words = AllWords(el!);
            Assert.Equal("Hello", words[0].Text);
            Assert.Equal("World", words[1].Text);
        }

        [Fact]
        public async Task Capitalize_OnlyCapitalizesFirstLetterOfWhitespaceDelimitedWord()
        {
            // "editor-in-chief" is one whitespace-delimited word; capitalize should only affect
            // its leading letter, not each fragment the line-breaker later splits it into at hyphens.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="text-transform: capitalize">editor-in-chief</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            var combined = string.Concat(AllWords(el!).Select(w => w.Text));
            Assert.Equal("Editor-in-chief", combined);
        }

        [Fact]
        public async Task None_LeavesTextUnchanged()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="text-transform: none">Hello World</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("Hello", FindFirstWord(el!)!.Text);
        }

        [Fact]
        public async Task Default_LeavesTextUnchanged()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el">Hello World</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("Hello", FindFirstWord(el!)!.Text);
        }

        [Fact]
        public async Task InheritsFromParent_ChildTextTransformedWithoutRedeclaring()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="text-transform: uppercase">
                  <span id="child">hello</span>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("HELLO", FindFirstWord(child!)!.Text);
        }

        [Fact]
        public async Task Inherit_Keyword_ResolvesToParentValue()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="text-transform: uppercase">
                  <span id="child" style="text-transform: inherit">hello</span>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("uppercase", child!.TextTransform);
        }

        [Fact]
        public async Task Initial_Keyword_ResetsToNoneIgnoringParent()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="text-transform: uppercase">
                  <span id="child" style="text-transform: initial">hello</span>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("none", child!.TextTransform);
        }

        [Fact]
        public async Task Unset_OnInheritedProperty_ResolvesToParentValue()
        {
            // text-transform is inherited, so "unset" behaves like "inherit".
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="text-transform: uppercase">
                  <span id="child" style="text-transform: unset">hello</span>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("uppercase", child!.TextTransform);
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static async Task<CssBox> BuildBoxTree(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }

        private static CssRect? FindFirstWord(CssBox box)
        {
            if (box.Words.Count > 0) return box.Words[0];
            foreach (var child in box.Boxes)
            {
                var found = FindFirstWord(child);
                if (found is not null) return found;
            }
            return null;
        }

        private static List<CssRect> AllWords(CssBox box)
        {
            var words = new List<CssRect>();
            CollectWords(box, words);
            return words;
        }

        private static void CollectWords(CssBox box, List<CssRect> words)
        {
            words.AddRange(box.Words);
            foreach (var child in box.Boxes)
                CollectWords(child, words);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (box.HtmlTag?.Attributes?.TryGetValue("id", out var boxId) == true
                && string.Equals(boxId, id, StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }

            return null;
        }
    }
}
