using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using System.Collections.Generic;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct unit tests for <see cref="MarginBoxRenderer.ResolveNamedString"/> — the pure function
    /// that resolves <c>string(name, first/last/start/first-except)</c> against a document-level list
    /// of <see cref="NamedString"/> entries for a given page. Exercised directly (rather than through
    /// full HTML→PDF generation + rendered-text extraction) for the same reason as
    /// PdfGeneratorSelectPageRuleTests: PeachPDF embeds subsetted fonts, so a decoded PDF content
    /// stream's Tj operands are typically glyph indices, not literal ASCII text.
    ///
    /// This is the direct regression coverage for the string-set Y-timing bug: every NamedString used
    /// to register at Y=0 (CssNamedStringEngine.ApplyStringSet ran before CssBox.Location was computed
    /// for that layout pass), so this page-range matching only worked by accident on page 1.
    ///
    /// <see cref="Resolve"/> stands in for production's <c>htmlContainer.SlotStartingAt</c>/
    /// <c>currentPageIndex</c> pair with a plain division against a fixed <c>pageHeight</c> — the same
    /// non-variable-geometry math <see cref="PeachPDF.Html.Core.HtmlContainerInt.PageIndexOf"/> itself falls back
    /// to when the document uses one uniform page size.
    /// </summary>
    public class MarginBoxResolveNamedStringTests
    {
        // Three pages of height 800: [0,800), [800,1600), [1600,2400)

        private static string Resolve(string name, string keyword, double pageY, double pageHeight, IReadOnlyList<NamedString> namedStrings) =>
            MarginBoxRenderer.ResolveNamedString(name, keyword, (int)(pageY / pageHeight), y => (int)(y / pageHeight), namedStrings);

        [Fact]
        public void First_ReturnsFirstAssignmentOnThisPage()
        {
            var namedStrings = new List<NamedString>
            {
                new("term", "Apple", 50),
                new("term", "Avocado", 300),
                new("term", "Banana", 900),
            };

            var result = Resolve("term", "first", pageY: 800, pageHeight: 800, namedStrings);

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
            var result = Resolve("term", "first", pageY: 800, pageHeight: 800, namedStrings);

            Assert.Equal("Avocado", result);
        }

        [Fact]
        public void First_ReturnsEmpty_WhenNoAssignmentExistsYet()
        {
            var namedStrings = new List<NamedString>
            {
                new("term", "Banana", 900),
            };

            var result = Resolve("term", "first", pageY: 0, pageHeight: 800, namedStrings);

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

            var result = Resolve("term", "last", pageY: 800, pageHeight: 800, namedStrings);

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

            var result = Resolve("term", "last", pageY: 800, pageHeight: 800, namedStrings);

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

            var result = Resolve("term", "start", pageY: 800, pageHeight: 800, namedStrings);

            Assert.Equal("Avocado", result);
        }

        [Fact]
        public void Start_ReturnsEmpty_WhenNoAssignmentBeforeThisPage()
        {
            var namedStrings = new List<NamedString>
            {
                new("term", "Banana", 900),
            };

            var result = Resolve("term", "start", pageY: 800, pageHeight: 800, namedStrings);

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
            var result = Resolve("term", "first-except", pageY: 0, pageHeight: 800, namedStrings);

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void FirstExcept_ReturnsCarriedValue_OnLaterPagesWithNoNewAssignment()
        {
            var namedStrings = new List<NamedString>
            {
                new("term", "Apple", 50),
            };

            var result = Resolve("term", "first-except", pageY: 800, pageHeight: 800, namedStrings);

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

            var termResult = Resolve("term", "first", pageY: 0, pageHeight: 800, namedStrings);
            var letterResult = Resolve("letter", "first", pageY: 0, pageHeight: 800, namedStrings);

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

            Assert.Equal("Apple", Resolve("term", "first", pageY: 0, pageHeight: 800, namedStrings));
            Assert.Equal("Banana", Resolve("term", "first", pageY: 800, pageHeight: 800, namedStrings));
            Assert.Equal("Banana", Resolve("term", "first", pageY: 1600, pageHeight: 800, namedStrings));
        }

        // ─── ResolveContent's `string()` token handling (the real call site) ──────────────────────────

        // Real HtmlContainerInt.SlotStartingAt, rather than the plain-division stand-in above - this is
        // the actual production path from a `content: string(...)` margin-box declaration through to a
        // page-attributed value, including the SlotStartingAt/PageBoundaryEpsilon call ResolveContent
        // makes before delegating to ResolveNamedString.
        private static HtmlContainerInt Container(double pageHeight)
        {
            var container = new HtmlContainerInt(new PdfSharpAdapter())
            {
                MarginTop = 0,
                PageSize = new RSize(400, pageHeight),
            };
            return container;
        }

        [Fact]
        public void ResolveContent_StringFunction_ResolvesAgainstTheCurrentPage()
        {
            var namedStrings = new List<NamedString>
            {
                new("term", "Apple", 50),
                new("term", "Banana", 900),
            };

            var result = MarginBoxRenderer.ResolveContent("string(term)", pageNumber: 2, totalPages: 3,
                pageY: 800, Container(800), namedStrings);

            Assert.Equal("Banana", result);
        }

        [Fact]
        public void ResolveContent_StringFunction_HonoursTheLastKeyword()
        {
            var namedStrings = new List<NamedString>
            {
                new("term", "Apple", 50),
                new("term", "Avocado", 300),
                new("term", "Banana", 900),
            };

            var result = MarginBoxRenderer.ResolveContent("string(term, last)", pageNumber: 1, totalPages: 3,
                pageY: 0, Container(800), namedStrings);

            Assert.Equal("Avocado", result);
        }

        [Fact]
        public void ResolveContent_StringFunction_DoesNotLeakAColumnTopFromTheNextPage()
        {
            // Direct end-to-end coverage of the actual dictionary.html bug shape via the real call site:
            // two entries opening different columns of the *next* page share a Y right at this page's own
            // end boundary, and must not be picked as this page's "last".
            var namedStrings = new List<NamedString>
            {
                new("term", "last-on-this-page", 750),
                new("term", "column-1-top-of-next-page", 800),
                new("term", "column-2-top-of-next-page", 800),
            };

            var result = MarginBoxRenderer.ResolveContent("string(term, last)", pageNumber: 1, totalPages: 2,
                pageY: 0, Container(800), namedStrings);

            Assert.Equal("last-on-this-page", result);
        }

        [Fact]
        public void ResolveContent_MixesLiteralsCountersAndStringFunction()
        {
            var namedStrings = new List<NamedString> { new("term", "Apple", 50) };

            var result = MarginBoxRenderer.ResolveContent("\"p. \" counter(page) \": \" string(term)",
                pageNumber: 4, totalPages: 10, pageY: 0, Container(800), namedStrings);

            Assert.Equal("p. 4: Apple", result);
        }
    }
}
