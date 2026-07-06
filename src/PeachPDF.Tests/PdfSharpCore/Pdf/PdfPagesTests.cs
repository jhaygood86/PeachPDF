using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class PdfPagesTests
    {
        [Fact]
        public void Add_IncrementsCountAndReturnsNewPage()
        {
            var doc = new PdfDocument();

            var page = doc.Pages.Add();

            Assert.NotNull(page);
            Assert.Equal(1, doc.Pages.Count);
        }

        [Fact]
        public void Indexer_ReturnsPageAtPosition()
        {
            var doc = new PdfDocument();
            var first = doc.Pages.Add();
            var second = doc.Pages.Add();

            Assert.Same(first, doc.Pages[0]);
            Assert.Same(second, doc.Pages[1]);
        }

        [Fact]
        public void Indexer_OutOfRange_Throws()
        {
            var doc = new PdfDocument();
            doc.Pages.Add();

            Assert.Throws<ArgumentOutOfRangeException>(() => doc.Pages[5]);
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.Pages[-1]);
        }

        [Fact]
        public void Insert_AtSpecificIndex_ShiftsLaterPages()
        {
            var doc = new PdfDocument();
            var first = doc.Pages.Add();
            var third = doc.Pages.Add();

            var second = doc.Pages.Insert(1);

            Assert.Same(first, doc.Pages[0]);
            Assert.Same(second, doc.Pages[1]);
            Assert.Same(third, doc.Pages[2]);
        }

        [Fact]
        public void FindPage_ExistingId_ReturnsPage()
        {
            var doc = new PdfDocument();
            var page = doc.Pages.Add();

            var found = doc.Pages.FindPage(page.ObjectID);

            Assert.NotNull(found);
            Assert.Equal(page.ObjectID, found.ObjectID);
        }

        [Fact]
        public void FindPage_UnknownId_ReturnsNull()
        {
            var doc = new PdfDocument();
            doc.Pages.Add();

            var found = doc.Pages.FindPage(new PdfObjectID(999));

            Assert.Null(found);
        }

        [Fact]
        public void Remove_RemovesPageAndUpdatesCount()
        {
            var doc = new PdfDocument();
            var page = doc.Pages.Add();
            doc.Pages.Add();

            doc.Pages.Remove(page);

            Assert.Equal(1, doc.Pages.Count);
        }

        [Fact]
        public void RemoveAt_RemovesPageAtIndex()
        {
            var doc = new PdfDocument();
            doc.Pages.Add();
            var second = doc.Pages.Add();

            doc.Pages.RemoveAt(0);

            Assert.Equal(1, doc.Pages.Count);
            Assert.Same(second, doc.Pages[0]);
        }

        [Fact]
        public void MovePage_ReordersPages()
        {
            var doc = new PdfDocument();
            var first = doc.Pages.Add();
            var second = doc.Pages.Add();

            doc.Pages.MovePage(0, 1);

            Assert.Same(second, doc.Pages[0]);
            Assert.Same(first, doc.Pages[1]);
        }

        [Fact]
        public void MovePage_SameIndex_IsNoOp()
        {
            var doc = new PdfDocument();
            var first = doc.Pages.Add();
            doc.Pages.Add();

            doc.Pages.MovePage(0, 0);

            Assert.Same(first, doc.Pages[0]);
        }

        [Fact]
        public void MovePage_OutOfRange_Throws()
        {
            var doc = new PdfDocument();
            doc.Pages.Add();

            Assert.Throws<ArgumentOutOfRangeException>(() => doc.Pages.MovePage(0, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.Pages.MovePage(5, 0));
        }

        [Fact]
        public void InsertingSamePageTwice_Throws()
        {
            var doc = new PdfDocument();
            var page = doc.Pages.Add();

            Assert.Throws<InvalidOperationException>(() => doc.Pages.Insert(0, page));
        }

        [Fact]
        public void Insert_NullPage_Throws()
        {
            var doc = new PdfDocument();

            Assert.Throws<ArgumentNullException>(() => doc.Pages.Insert(0, null!));
        }
    }
}
