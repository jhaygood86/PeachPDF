using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;

namespace PeachPDF.Tests.Html.Core.Dom
{
    public class CssContentEngineTests
    {
        [Fact]
        public void ApplyContent_WithStringLiteral_SetsText()
        {
            var box = CreateBox();
            box.Content = "\"Hello World\"";

            CssContentEngine.ApplyContent(box);

            Assert.Equal("Hello World", box.Text);
        }

        [Fact]
        public void ApplyContent_WithMultipleStringLiterals_Concatenates()
        {
            var box = CreateBox();
            box.Content = "\"Hello\" \" \" \"World\"";

            CssContentEngine.ApplyContent(box);

            Assert.Equal("Hello World", box.Text);
        }

        [Fact]
        public void ApplyContent_WithNone_DoesNotSetText()
        {
            var box = CreateBox();
            box.Content = "none";

            CssContentEngine.ApplyContent(box);

            Assert.Null(box.Text);
        }

        [Fact]
        public void ApplyContent_WithNormal_DoesNotSetText()
        {
            var box = CreateBox();
            box.Content = "normal";

            CssContentEngine.ApplyContent(box);

            Assert.Null(box.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunction_EvaluatesNamedString()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "string(chapter)";

            // Register a named string in the container
            container.RegisterNamedString(new NamedString("chapter", "Introduction"));

            CssContentEngine.ApplyContent(box);

            Assert.Equal("Introduction", box.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunctionFirstKeyword_RetrievesFirst()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "string(chapter, first)";

            // Register multiple named strings with same name
            container.RegisterNamedString(new NamedString("chapter", "First Chapter"));
            container.RegisterNamedString(new NamedString("chapter", "Second Chapter"));

            CssContentEngine.ApplyContent(box);

            Assert.Equal("First Chapter", box.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunctionLastKeyword_RetrievesLast()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "string(chapter, last)";

            // Register multiple named strings with same name
            container.RegisterNamedString(new NamedString("chapter", "First Chapter"));
            container.RegisterNamedString(new NamedString("chapter", "Second Chapter"));

            CssContentEngine.ApplyContent(box);

            Assert.Equal("Second Chapter", box.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunctionNonExistent_ReturnsEmpty()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "string(nonexistent)";

            CssContentEngine.ApplyContent(box);

            Assert.Equal("", box.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunctionAndLiteral_Concatenates()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "\"Chapter: \" string(chapter)";

            container.RegisterNamedString(new NamedString("chapter", "Introduction"));

            CssContentEngine.ApplyContent(box);

            Assert.Equal("Chapter: Introduction", box.Text);
        }

        [Fact]
        public void ApplyContent_WithMultipleStringFunctions_ConcatenatesAll()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "string(chapter) \" / \" string(section)";

            container.RegisterNamedString(new NamedString("chapter", "Chapter One"));
            container.RegisterNamedString(new NamedString("section", "Section A"));

            CssContentEngine.ApplyContent(box);

            Assert.Equal("Chapter One / Section A", box.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunctionAndCounter_CombinesCorrectly()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "string(chapter) \" - Page \" counter(page)";
            box.CounterIncrement = "page";

            container.RegisterNamedString(new NamedString("chapter", "Introduction"));

            CssContentEngine.ApplyContent(box);

            Assert.Contains("Introduction - Page", box.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunctionStartKeyword_RetrievesFirst()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "string(chapter, start)";

            container.RegisterNamedString(new NamedString("chapter", "First"));
            container.RegisterNamedString(new NamedString("chapter", "Second"));

            CssContentEngine.ApplyContent(box);

            // Start should behave like first for now
            Assert.Equal("First", box.Text);
        }

        [Fact]
        public void ApplyContent_WithStringFunctionFirstExceptKeyword_ReturnsEmpty()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "string(chapter, first-except)";

            container.RegisterNamedString(new NamedString("chapter", "Chapter"));

            CssContentEngine.ApplyContent(box);

            // First-except not fully implemented, should return empty
            Assert.Equal("", box.Text);
        }

        [Fact]
        public void ApplyContent_WithAttrFunction_RetrievesAttribute()
        {
            var box = CreateBox();
            box.HtmlTag.Attributes["title"] = "Test Title";
            box.Content = "attr(title)";

            CssContentEngine.ApplyContent(box);

            Assert.Equal("Test Title", box.Text);
        }

        [Fact]
        public void ApplyContent_ComplexCombination_EvaluatesCorrectly()
        {
            var container = CreateContainer();
            var box = CreateBox(container);
            box.Content = "\"Part \" string(part) \" - Chapter \" string(chapter)";

            container.RegisterNamedString(new NamedString("part", "I"));
            container.RegisterNamedString(new NamedString("chapter", "1"));

            CssContentEngine.ApplyContent(box);

            Assert.Equal("Part I - Chapter 1", box.Text);
        }

        private CssBox CreateBox()
        {
            var tag = new HtmlTag("div", false, new Dictionary<string, string>());
            return new CssBox(null, tag);
        }

        private CssBox CreateBox(HtmlContainerInt container)
        {
            var tag = new HtmlTag("div", false, new Dictionary<string, string>());
            var box = new CssBox(null, tag);
            box.HtmlContainer = container;
            return box;
        }

        private HtmlContainerInt CreateContainer()
        {
            var adapter = new PdfSharpAdapter();
            return new HtmlContainerInt(adapter);
        }
    }
}
