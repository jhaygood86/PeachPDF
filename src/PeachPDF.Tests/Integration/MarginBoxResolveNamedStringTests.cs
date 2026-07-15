using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using System.Collections.Generic;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct unit tests for <see cref="MarginBoxRenderer.ResolveNamedString"/> — the pure function
    /// that resolves <c>string(name, first/last/start/first-except)</c> against a document-level list
    /// of <see cref="NamedString"/> entries for a given page's Y-range. Exercised directly (rather than
    /// through full HTML→PDF generation + rendered-text extraction) for the same reason as
    /// PdfGeneratorSelectPageRuleTests: PeachPDF embeds subsetted fonts, so a decoded PDF content
    /// stream's Tj operands are typically glyph indices, not literal ASCII text.
    ///
    /// This is the direct regression coverage for the string-set Y-timing bug: every NamedString used
    /// to register at Y=0 (CssNamedStringEngine.ApplyStringSet ran before CssBox.Location was computed
    /// for that layout pass), so this page-range matching only worked by accident on page 1.
    /// </summary>
    public class MarginBoxResolveNamedStringTests
    {
        // Three pages of height 800: [0,800), [800,1600), [1600,2400)

        [Fact]
        public void First_ReturnsFirstAssignmentOnThisPage()
        {
            var namedStrings = new List<NamedString>
            {
                new("term", "Apple", 50),
                new("term", "Avocado", 300),
                new("term", "Banana", 900),
            };

            var result = MarginBoxRenderer.ResolveNamedString("term", "first", pageY: 800, pageHeight: 800, namedStrings);

            Assert.Equal("Banana", result);
        }

        [Fact]
        public void First_FallsBackToLastAssignmentBeforeThisPage_WhenNoneOnThisPage()
        {
            var namedStrings = new List<NamedString>
            {
                new("term", "Apple", 50),
                new("term", "Avocado", 300),
            };

            // Page 2 [800,1600) has no "term" assignment of its own.
            var result = MarginBoxRenderer.ResolveNamedString("term", "first", pageY: 800, pageHeight: 800, namedStrings);

            Assert.Equal("Avocado", result);
        }

        [Fact]
        public void First_ReturnsEmpty_WhenNoAssignmentExistsYet()
        {
            var namedStrings = new List<NamedString>
            {
                new("term", "Banana", 900),
            };

            var result = MarginBoxRenderer.ResolveNamedString("term", "first", pageY: 0, pageHeight: 800, namedStrings);

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void Last_ReturnsLastAssignmentOnThisPage()
        {
            var namedStrings = new List<NamedString>
            {
                new("term", "Apple", 50),
                new("term", "Avocado", 300),
                new("term", "Banana", 900),
                new("term", "Cherry", 1200),
            };

            var result = MarginBoxRenderer.ResolveNamedString("term", "last", pageY: 800, pageHeight: 800, namedStrings);

            Assert.Equal("Cherry", result);
        }

        [Fact]
        public void Last_FallsBackToLastAssignmentBeforeThisPage_WhenNoneOnThisPage()
        {
            var namedStrings = new List<NamedString>
            {
                new("term", "Apple", 50),
                new("term", "Avocado", 300),
            };

            var result = MarginBoxRenderer.ResolveNamedString("term", "last", pageY: 800, pageHeight: 800, namedStrings);

            Assert.Equal("Avocado", result);
        }

        [Fact]
        public void Start_ReturnsLastAssignmentBeforeThisPage_EvenIfNewerAssignmentExistsOnThisPage()
        {
            var namedStrings = new List<NamedString>
            {
                new("term", "Apple", 50),
                new("term", "Avocado", 300),
                new("term", "Banana", 900),
            };

            var result = MarginBoxRenderer.ResolveNamedString("term", "start", pageY: 800, pageHeight: 800, namedStrings);

            Assert.Equal("Avocado", result);
        }

        [Fact]
        public void Start_ReturnsEmpty_WhenNoAssignmentBeforeThisPage()
        {
            var namedStrings = new List<NamedString>
            {
                new("term", "Banana", 900),
            };

            var result = MarginBoxRenderer.ResolveNamedString("term", "start", pageY: 800, pageHeight: 800, namedStrings);

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void FirstExcept_ReturnsEmpty_OnThePageWhereFirstAssigned()
        {
            var namedStrings = new List<NamedString>
            {
                new("term", "Apple", 50),
            };

            // Page 1 [0,800) is exactly where "Apple" was first assigned.
            var result = MarginBoxRenderer.ResolveNamedString("term", "first-except", pageY: 0, pageHeight: 800, namedStrings);

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void FirstExcept_ReturnsCarriedValue_OnLaterPagesWithNoNewAssignment()
        {
            var namedStrings = new List<NamedString>
            {
                new("term", "Apple", 50),
            };

            var result = MarginBoxRenderer.ResolveNamedString("term", "first-except", pageY: 800, pageHeight: 800, namedStrings);

            Assert.Equal("Apple", result);
        }

        [Fact]
        public void DifferentNames_DoNotInterfere()
        {
            var namedStrings = new List<NamedString>
            {
                new("term", "Apple", 50),
                new("letter", "A", 60),
            };

            var termResult = MarginBoxRenderer.ResolveNamedString("term", "first", pageY: 0, pageHeight: 800, namedStrings);
            var letterResult = MarginBoxRenderer.ResolveNamedString("letter", "first", pageY: 0, pageHeight: 800, namedStrings);

            Assert.Equal("Apple", termResult);
            Assert.Equal("A", letterResult);
        }

        [Fact]
        public void NamedString_MultiPage_LaterAssignment_OverridesOnItsOwnPageOnward()
        {
            // Regression shape mirroring the real bug: a value registered on page 1 must not leak onto
            // page 3 once a newer value was assigned on page 2, but must still carry forward from page 2
            // onto page 2's own later content with no new assignment.
            var namedStrings = new List<NamedString>
            {
                new("term", "Apple", 50),
                new("term", "Banana", 900),
            };

            Assert.Equal("Apple", MarginBoxRenderer.ResolveNamedString("term", "first", pageY: 0, pageHeight: 800, namedStrings));
            Assert.Equal("Banana", MarginBoxRenderer.ResolveNamedString("term", "first", pageY: 800, pageHeight: 800, namedStrings));
            Assert.Equal("Banana", MarginBoxRenderer.ResolveNamedString("term", "first", pageY: 1600, pageHeight: 800, namedStrings));
        }
    }
}
