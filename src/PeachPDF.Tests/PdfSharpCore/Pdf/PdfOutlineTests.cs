using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class PdfOutlineTests
    {
        [Fact]
        public void Add_TitleAndPage_SetsProperties()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            var outline = doc.Outlines.Add("Chapter 1", page);

            Assert.Equal("Chapter 1", outline.Title);
            Assert.Same(page, outline.DestinationPage);
        }

        [Fact]
        public void Add_WithOpenedFlag_SetsOpened()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            var outline = doc.Outlines.Add("Chapter", page, opened: true);

            Assert.True(outline.Opened);
        }

        [Fact]
        public void Add_WithStyle_SetsStyle()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            var outline = doc.Outlines.Add("Chapter", page, false, PdfOutlineStyle.Bold);

            Assert.Equal(PdfOutlineStyle.Bold, outline.Style);
        }

        [Fact]
        public void Add_WithTextColor_SetsTextColor()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            var outline = doc.Outlines.Add("Chapter", page, false, PdfOutlineStyle.Regular, XColors.Red);

            Assert.Equal(XColors.Red, outline.TextColor);
        }

        [Fact]
        public void Add_SetsParentToOutlinesRoot()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            var outline = doc.Outlines.Add("Chapter", page);

            Assert.NotNull(outline.Parent);
        }

        [Fact]
        public void Add_NestedOutline_HasChildren()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var parent = doc.Outlines.Add("Parent", page);

            parent.Outlines.Add("Child", page);

            Assert.True(parent.HasChildren);
            Assert.Equal(1, parent.Outlines.Count);
        }

        [Fact]
        public void PositionProperties_DefaultToNaN()
        {
            var outline = new PdfOutline();

            Assert.True(double.IsNaN(outline.Left));
            Assert.True(double.IsNaN(outline.Top));
            Assert.True(double.IsNaN(outline.Right));
            Assert.True(double.IsNaN(outline.Bottom));
            Assert.True(double.IsNaN(outline.Zoom));
        }

        [Fact]
        public void PositionProperties_RoundTrip()
        {
            var outline = new PdfOutline
            {
                Left = 1,
                Top = 2,
                Right = 3,
                Bottom = 4,
                Zoom = 1.5
            };

            Assert.Equal(1, outline.Left);
            Assert.Equal(2, outline.Top);
            Assert.Equal(3, outline.Right);
            Assert.Equal(4, outline.Bottom);
            Assert.Equal(1.5, outline.Zoom);
        }

        [Fact]
        public void PageDestinationType_DefaultsToXyz()
        {
            var outline = new PdfOutline();

            Assert.Equal(PdfPageDestinationType.Xyz, outline.PageDestinationType);
        }

        [Fact]
        public void PageDestinationType_RoundTrips()
        {
            var outline = new PdfOutline { PageDestinationType = PdfPageDestinationType.Fit };

            Assert.Equal(PdfPageDestinationType.Fit, outline.PageDestinationType);
        }

        [Fact]
        public void HasChildren_NoChildren_ReturnsFalse()
        {
            var outline = new PdfOutline();

            Assert.False(outline.HasChildren);
        }
    }

    public class PdfOutlineCollectionTests
    {
        [Fact]
        public void Add_IncreasesCount()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            doc.Outlines.Add("A", page);

            Assert.Equal(1, doc.Outlines.Count);
        }

        [Fact]
        public void Add_Null_Throws()
        {
            var doc = new PdfDocument();

            Assert.Throws<ArgumentNullException>(() => doc.Outlines.Add(null!));
        }

        [Fact]
        public void Contains_FindsAddedOutline()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var outline = doc.Outlines.Add("A", page);

            Assert.True(doc.Outlines.Contains(outline));
        }

        [Fact]
        public void IndexOf_ReturnsPosition()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            doc.Outlines.Add("A", page);
            var second = doc.Outlines.Add("B", page);

            Assert.Equal(1, doc.Outlines.IndexOf(second));
        }

        [Fact]
        public void Indexer_ReturnsOutlineAtPosition()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var first = doc.Outlines.Add("A", page);

            Assert.Same(first, doc.Outlines[0]);
        }

        [Fact]
        public void Indexer_OutOfRange_Throws()
        {
            var doc = new PdfDocument();

            Assert.Throws<ArgumentOutOfRangeException>(() => doc.Outlines[0]);
        }

        [Fact]
        public void Remove_RemovesOutline()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var outline = doc.Outlines.Add("A", page);

            var removed = doc.Outlines.Remove(outline);

            Assert.True(removed);
            Assert.Equal(0, doc.Outlines.Count);
        }

        [Fact]
        public void Remove_NotInCollection_ReturnsFalse()
        {
            var doc = new PdfDocument();
            var outline = new PdfOutline("Standalone", null!);

            Assert.False(doc.Outlines.Remove(outline));
        }

        [Fact]
        public void RemoveAt_RemovesOutlineAtIndex()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            doc.Outlines.Add("A", page);

            doc.Outlines.RemoveAt(0);

            Assert.Equal(0, doc.Outlines.Count);
        }

        [Fact]
        public void Clear_RemovesAllOutlines()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            doc.Outlines.Add("A", page);
            doc.Outlines.Add("B", page);

            doc.Outlines.Clear();

            Assert.Equal(0, doc.Outlines.Count);
        }

        [Fact]
        public void CopyTo_CopiesAllOutlines()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            doc.Outlines.Add("A", page);
            doc.Outlines.Add("B", page);

            var array = new PdfOutline[2];
            doc.Outlines.CopyTo(array, 0);

            Assert.Equal("A", array[0].Title);
            Assert.Equal("B", array[1].Title);
        }

        [Fact]
        public void GetEnumerator_EnumeratesAllOutlines()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            doc.Outlines.Add("A", page);
            doc.Outlines.Add("B", page);

            var titles = new List<string>();
            foreach (var outline in doc.Outlines)
                titles.Add(outline.Title);

            Assert.Equal(["A", "B"], titles);
        }

        [Fact]
        public void IsReadOnly_IsAlwaysFalse()
        {
            var doc = new PdfDocument();

            Assert.False(doc.Outlines.IsReadOnly);
        }

        [Fact]
        public void Insert_PlacesOutlineAtIndex()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            doc.Outlines.Add("A", page);
            var toInsert = new PdfOutline("B", page);

            doc.Outlines.Insert(0, toInsert);

            Assert.Same(toInsert, doc.Outlines[0]);
        }

        [Fact]
        public void IndexerSetter_ReplacesOutlineAtIndex()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            doc.Outlines.Add("A", page);
            var replacement = new PdfOutline("Replacement", page);

            doc.Outlines[0] = replacement;

            Assert.Same(replacement, doc.Outlines[0]);
        }
    }
}
