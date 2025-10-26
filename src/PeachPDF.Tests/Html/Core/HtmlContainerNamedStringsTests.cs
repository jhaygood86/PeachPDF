using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Entities;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// Tests for document-level named string storage in HtmlContainerInt.
    /// Verifies the CSS GCPM string-set property document storage functionality.
    /// </summary>
    public class HtmlContainerNamedStringsTests
    {
        [Fact]
        public void HtmlContainer_InitialState_HasEmptyNamedStrings()
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);

            Assert.Empty(container.NamedStrings);
        }

        [Fact]
        public void RegisterNamedString_SingleString_AddsToCollection()
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            var namedString = new NamedString("chapter", "Introduction");

            container.RegisterNamedString(namedString);

            Assert.Single(container.NamedStrings);
            Assert.Equal("chapter", container.NamedStrings[0].Name);
            Assert.Equal("Introduction", container.NamedStrings[0].Value);
        }

        [Fact]
        public void RegisterNamedString_MultipleStrings_MaintainsDocumentOrder()
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);

            var first = new NamedString("chapter", "Chapter 1");
            var second = new NamedString("section", "Section A");
            var third = new NamedString("chapter", "Chapter 2");

            container.RegisterNamedString(first);
            container.RegisterNamedString(second);
            container.RegisterNamedString(third);

            Assert.Equal(3, container.NamedStrings.Count);
            Assert.Equal("Chapter 1", container.NamedStrings[0].Value);
            Assert.Equal("Section A", container.NamedStrings[1].Value);
            Assert.Equal("Chapter 2", container.NamedStrings[2].Value);
        }

        [Fact]
        public void RegisterNamedString_SameNameMultipleTimes_AllowsDuplicates()
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);

            var first = new NamedString("chapter", "First");
            var second = new NamedString("chapter", "Second");
            var third = new NamedString("chapter", "Third");

            container.RegisterNamedString(first);
            container.RegisterNamedString(second);
            container.RegisterNamedString(third);

            Assert.Equal(3, container.NamedStrings.Count);
            Assert.All(container.NamedStrings, ns => Assert.Equal("chapter", ns.Name));
        }

        [Fact]
        public void ClearNamedStrings_RemovesAllStrings()
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);

            container.RegisterNamedString(new NamedString("chapter", "Chapter 1"));
            container.RegisterNamedString(new NamedString("section", "Section A"));

            Assert.Equal(2, container.NamedStrings.Count);

            container.ClearNamedStrings();

            Assert.Empty(container.NamedStrings);
        }

        [Fact]
        public async Task Clear_ClearsNamedStrings()
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);

            // SetHtml with empty HTML initializes Root
            await container.SetHtml("<html><body>test</body></html>", null);

            // Now register after SetHtml
            container.RegisterNamedString(new NamedString("chapter", "Chapter 1"));

            Assert.Single(container.NamedStrings);

            // Setting new HTML should clear named strings
            await container.SetHtml("<html><body>new</body></html>", null);

            Assert.Empty(container.NamedStrings);
        }

        [Fact]
        public void NamedStrings_ReturnsReadOnlyCollection()
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);

            var namedStrings = container.NamedStrings;

            Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyList<NamedString>>(namedStrings);
        }

        [Fact]
        public void RegisterNamedString_PreservesNameAndValue()
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            var originalString = new NamedString("my-custom-string", "Custom Value");

            container.RegisterNamedString(originalString);

            var retrieved = container.NamedStrings[0];
            Assert.Equal("my-custom-string", retrieved.Name);
            Assert.Equal("Custom Value", retrieved.Value);
        }

        [Fact]
        public void RegisterNamedString_WithEmptyValue_AllowsEmptyString()
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            var namedString = new NamedString("empty", "");

            container.RegisterNamedString(namedString);

            Assert.Single(container.NamedStrings);
            Assert.Equal("", container.NamedStrings[0].Value);
        }

        [Fact]
        public void RegisterNamedString_MultipleWithDifferentNames_AllStored()
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);

            container.RegisterNamedString(new NamedString("chapter", "Ch 1"));
            container.RegisterNamedString(new NamedString("section", "Sec A"));
            container.RegisterNamedString(new NamedString("page-header", "Header"));
            container.RegisterNamedString(new NamedString("page-footer", "Footer"));

            Assert.Equal(4, container.NamedStrings.Count);
            var names = container.NamedStrings.Select(ns => ns.Name).ToArray();
            Assert.Contains("chapter", names);
            Assert.Contains("section", names);
            Assert.Contains("page-header", names);
            Assert.Contains("page-footer", names);
        }

        [Fact]
        public void RegisterNamedString_DocumentOrderPreserved_FirstLastRetrieval()
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);

            // Simulate document order: first chapter, then second chapter
            container.RegisterNamedString(new NamedString("chapter", "First Chapter"));
            container.RegisterNamedString(new NamedString("chapter", "Second Chapter"));

            // First in document order
            var firstChapter = container.NamedStrings.FirstOrDefault(ns => ns.Name == "chapter");
            Assert.NotNull(firstChapter);
            Assert.Equal("First Chapter", firstChapter.Value);

            // Last in document order
            var lastChapter = container.NamedStrings.LastOrDefault(ns => ns.Name == "chapter");
            Assert.NotNull(lastChapter);
            Assert.Equal("Second Chapter", lastChapter.Value);
        }
    }
}
