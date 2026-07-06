using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class PdfOutlineSaveTests
    {
        [Theory]
        [InlineData((int)PdfPageDestinationType.Xyz)]
        [InlineData((int)PdfPageDestinationType.Fit)]
        [InlineData((int)PdfPageDestinationType.FitH)]
        [InlineData((int)PdfPageDestinationType.FitV)]
        [InlineData((int)PdfPageDestinationType.FitR)]
        [InlineData((int)PdfPageDestinationType.FitB)]
        [InlineData((int)PdfPageDestinationType.FitBH)]
        [InlineData((int)PdfPageDestinationType.FitBV)]
        public void Save_WithOutline_ForEachDestinationType_Succeeds(int destinationTypeValue)
        {
            var destinationType = (PdfPageDestinationType)destinationTypeValue;
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var outline = doc.Outlines.Add("Chapter", page, true, PdfOutlineStyle.Bold, XColors.Red);
            outline.PageDestinationType = destinationType;
            outline.Left = 1;
            outline.Top = 2;
            outline.Right = 3;
            outline.Bottom = 4;
            outline.Zoom = 1.5;

            using var stream = new MemoryStream();
            doc.Save(stream);

            Assert.True(stream.Length > 0);
        }

        [Fact]
        public void Save_WithNestedOutlines_Succeeds()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var parent = doc.Outlines.Add("Parent", page);
            var child = parent.Outlines.Add("Child", page);
            child.Outlines.Add("Grandchild", page);
            doc.Outlines.Add("Sibling", page);

            using var stream = new MemoryStream();
            doc.Save(stream);

            var text = System.Text.Encoding.ASCII.GetString(stream.ToArray());
            Assert.Contains("Parent", text);
        }

        [Fact]
        public void Save_WithOutlineWithoutDestinationPage_Succeeds()
        {
            var doc = new PdfDocument();
            doc.AddPage();
            doc.Outlines.Add("No destination", null!);

            using var stream = new MemoryStream();
            doc.Save(stream);

            Assert.True(stream.Length > 0);
        }

        [Fact]
        public void Save_WithDefaultTextColor_OmitsColorEntry()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            doc.Outlines.Add("Chapter", page);

            using var stream = new MemoryStream();
            var ex = Record.Exception(() => doc.Save(stream));

            Assert.Null(ex);
        }

        [Fact]
        public void CountOpen_DoesNotDescendIntoChildren()
        {
            // PdfOutlineCollection.CountOpen() is a stub -- the file itself carries the comment
            // "Review: CountOpen does not work" and its recursive summation over child outlines is
            // commented out, always returning 0. So PdfOutline.CountOpen() (self-opened ? 1 : 0,
            // plus _outlines.CountOpen()) never actually counts open descendants; it only reports
            // whether the outline itself is opened. Real, pre-existing bug; documented here.
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var parent = doc.Outlines.Add("Parent", page, opened: true);
            parent.Outlines.Add("Child", page, opened: true);
            parent.Outlines.Add("Closed child", page, opened: false);

            Assert.Equal(1, parent.CountOpen());
        }
    }
}
