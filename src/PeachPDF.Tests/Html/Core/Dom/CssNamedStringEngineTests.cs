using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;

namespace PeachPDF.Tests.Html.Core.Dom
{
    public class CssNamedStringEngineTests
    {
        [Fact]
        public void ApplyStringSet_None_DoesNotCreateNamedStrings()
        {
            // Arrange
            var box = CreateTestBox();
            box.StringSet = "none";

            // Act
            CssNamedStringEngine.ApplyStringSet(box);

            // Assert
            Assert.Empty(box.NamedStrings);
        }

        [Fact]
        public void ApplyStringSet_SimpleStringLiteral_CreatesNamedString()
        {
            // Arrange
            var box = CreateTestBox();
            box.StringSet = "header \"Page Header\"";

            // Act
            CssNamedStringEngine.ApplyStringSet(box);

            // Assert
            Assert.Single(box.NamedStrings);
            Assert.True(box.NamedStrings.ContainsKey("header"));
            Assert.Equal("Page Header", box.NamedStrings["header"].Value);
        }

        [Fact]
        public void ApplyStringSet_MultipleStringLiterals_ConcatenatesValues()
        {
            // Arrange
            var box = CreateTestBox();
            box.StringSet = "header \"Page \" \"Header\"";

            // Act
            CssNamedStringEngine.ApplyStringSet(box);

            // Assert
            Assert.Single(box.NamedStrings);
            Assert.Equal("Page Header", box.NamedStrings["header"].Value);
        }

        [Fact]
        public void ApplyStringSet_MultiplePairs_CreatesMultipleNamedStrings()
        {
            // Arrange
            var box = CreateTestBox();
            box.StringSet = "header \"Page Header\", footer \"Page Footer\"";

            // Act
            CssNamedStringEngine.ApplyStringSet(box);

            // Assert
            Assert.Equal(2, box.NamedStrings.Count);
            Assert.Equal("Page Header", box.NamedStrings["header"].Value);
            Assert.Equal("Page Footer", box.NamedStrings["footer"].Value);
        }

        [Fact]
        public void ApplyStringSet_WithCounter_IncludesCounterValue()
        {
            // Arrange
            var box = CreateTestBox();
            // Don't set CounterIncrement - just set the counter value directly
            box.Counters["page"] = new CssCounter("page", 5, false, true, null);
            box.StringSet = "pagelabel \"Page \" counter(page)";

            // Act
            CssNamedStringEngine.ApplyStringSet(box);

            // Assert
            Assert.Single(box.NamedStrings);
            Assert.Equal("Page 5", box.NamedStrings["pagelabel"].Value);
        }

        [Fact]
        public void ApplyStringSet_WithAttr_IncludesAttributeValue()
        {
            // Arrange
            var tag = new HtmlTag("div", false, new Dictionary<string, string>
     {
          { "title", "Test Title" }
            });
            var box = CreateTestBox(tag);
            box.StringSet = "heading attr(title)";

            // Act
            CssNamedStringEngine.ApplyStringSet(box);

            // Assert
            Assert.Single(box.NamedStrings);
            Assert.Equal("Test Title", box.NamedStrings["heading"].Value);
        }

        [Fact]
        public void ApplyStringSet_WithContentText_IncludesElementText()
        {
            // Arrange
            var box = CreateTestBox();
            box.Text = "  Hello   World  ";
            box.StringSet = "heading content(text)";

            // Act
            CssNamedStringEngine.ApplyStringSet(box);

            // Assert
            Assert.Single(box.NamedStrings);
            Assert.Equal("Hello World", box.NamedStrings["heading"].Value); // Whitespace normalized
        }

        [Fact]
        public void ApplyStringSet_WithContentFirstLetter_IncludesFirstLetter()
        {
            // Arrange
            var box = CreateTestBox();
            box.Text = "Hello World";
            box.StringSet = "initial content(first-letter)";

            // Act
            CssNamedStringEngine.ApplyStringSet(box);

            // Assert
            Assert.Single(box.NamedStrings);
            Assert.Equal("H", box.NamedStrings["initial"].Value);
        }

        [Fact]
        public void GetNamedString_ReturnsValueFromBox()
        {
            // Arrange
            var box = CreateTestBox();
            box.NamedStrings["test"] = new NamedString("test", "Test Value");

            // Act
            var result = CssNamedStringEngine.GetNamedString(box, "test");

            // Assert
            Assert.Equal("Test Value", result);
        }

        [Fact]
        public void GetNamedString_SearchesParentBoxes()
        {
            // Arrange
            var parentBox = CreateTestBox();
            parentBox.NamedStrings["test"] = new NamedString("test", "Parent Value");

            var childBox = new CssBox(parentBox, null);

            // Act
            var result = CssNamedStringEngine.GetNamedString(childBox, "test");

            // Assert
            Assert.Equal("Parent Value", result);
        }

        [Fact]
        public void GetNamedString_ReturnsEmptyStringIfNotFound()
        {
            // Arrange
            var box = CreateTestBox();

            // Act
            var result = CssNamedStringEngine.GetNamedString(box, "nonexistent");

            // Assert
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void ApplyStringSet_WithStringFunction_RetrievesNamedString()
        {
            // Arrange
            var parentBox = CreateTestBox();
            parentBox.NamedStrings["chapter"] = new NamedString("chapter", "Chapter 1");

            var box = new CssBox(parentBox, null);
            box.StringSet = "heading string(chapter)";

            // Act
            CssNamedStringEngine.ApplyStringSet(box);

            // Assert
            Assert.Single(box.NamedStrings);
            Assert.Equal("Chapter 1", box.NamedStrings["heading"].Value);
        }

        [Fact]
        public void ApplyStringSet_WithStringFunctionFirst_RetrievesFirstAssignment()
        {
            // Arrange
            var grandParentBox = CreateTestBox();
            grandParentBox.NamedStrings["chapter"] = new NamedString("chapter", "First");

            var parentBox = new CssBox(grandParentBox, null);
            parentBox.NamedStrings["chapter"] = new NamedString("chapter", "Second");

            var box = new CssBox(parentBox, null);
            box.StringSet = "heading string(chapter, first)";

            // Act
            CssNamedStringEngine.ApplyStringSet(box);

            // Assert
            Assert.Single(box.NamedStrings);
            Assert.Equal("First", box.NamedStrings["heading"].Value);
        }

        [Fact]
        public void ApplyStringSet_WithStringFunctionLast_RetrievesLastAssignment()
        {
            // Arrange
            var grandParentBox = CreateTestBox();
            grandParentBox.NamedStrings["chapter"] = new NamedString("chapter", "First");

            var parentBox = new CssBox(grandParentBox, null);
            parentBox.NamedStrings["chapter"] = new NamedString("chapter", "Second");

            var box = new CssBox(parentBox, null);
            box.StringSet = "heading string(chapter, last)";

            // Act
            CssNamedStringEngine.ApplyStringSet(box);

            // Assert
            Assert.Single(box.NamedStrings);
            Assert.Equal("Second", box.NamedStrings["heading"].Value);
        }

        [Fact]
        public void ApplyStringSet_WithStringFunctionAndStringLiteral_ConcatenatesCorrectly()
        {
            // Arrange
            var parentBox = CreateTestBox();
            parentBox.NamedStrings["chapter"] = new NamedString("chapter", "Introduction");

            var box = new CssBox(parentBox, null);
            box.StringSet = "heading \"Chapter: \" string(chapter)";

            // Act
            CssNamedStringEngine.ApplyStringSet(box);

            // Assert
            Assert.Single(box.NamedStrings);
            Assert.Equal("Chapter: Introduction", box.NamedStrings["heading"].Value);
        }

        [Fact]
        public void ApplyStringSet_WithStringFunctionNotFound_ReturnsEmptyString()
        {
            // Arrange
            var box = CreateTestBox();
            box.StringSet = "heading string(nonexistent)";

            // Act
            CssNamedStringEngine.ApplyStringSet(box);

            // Assert
            Assert.Single(box.NamedStrings);
            Assert.Equal(string.Empty, box.NamedStrings["heading"].Value);
        }

        private static CssBox CreateTestBox(HtmlTag? tag = null)
        {
            return new CssBox(null, tag);
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
