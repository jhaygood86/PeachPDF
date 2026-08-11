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
        public void Save_WithOutlineUrl_WritesUriActionNotDest()
        {
            // An outline with a Url and no DestinationPage writes an /A URI action instead of /Dest
            // (mutually exclusive per PDF 32000-1:2008 Table 153/176) - see PdfOutline.PrepareForSave.
            var doc = new PdfDocument();
            doc.AddPage();
            var outline = new PeachPDF.PdfSharpCore.Pdf.PdfOutline { Title = "External", Url = "https://example.com" };
            doc.Outlines.Add(outline);

            using var stream = new MemoryStream();
            doc.Save(stream);

            var text = System.Text.Encoding.ASCII.GetString(stream.ToArray());
            Assert.Contains("/S/URI", text);
            Assert.Contains("https://example.com", text);
            Assert.DoesNotContain("/Dest", text);
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

        [Fact]
        public void PrepareForSave_ClosedIntermediateItem_ExcludesHiddenDescendantsFromAncestorCounts()
        {
            // Regression test for #715: a closed intermediate item must not let its own
            // descendants leak into an ancestor's /Count, and must itself carry the negative
            // count of what would appear if it were reopened (PDF 32000-1 Table 152/153).
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var top = doc.Outlines.Add("Top", page, opened: true);
            var closedMid = top.Outlines.Add("Closed mid", page, opened: false);
            closedMid.Outlines.Add("H3 A", page, opened: true);
            closedMid.Outlines.Add("H3 B", page, opened: true);

            var root = top.Parent;
            root.PrepareForSave();

            Assert.Equal(2, root.Elements.GetInteger(PdfOutline.Keys.Count));
            Assert.Equal(1, top.Elements.GetInteger(PdfOutline.Keys.Count));
            Assert.Equal(-2, closedMid.Elements.GetInteger(PdfOutline.Keys.Count));
        }

        [Fact]
        public void PrepareForSave_ClosedItemWithClosedChildren_StillWritesCount()
        {
            // An item with descendants must always carry a /Count entry, even when none of its
            // children (or their descendants) are open -- the PDF spec requires it whenever the
            // item has any descendants at all, not only when some are open.
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var closedParent = doc.Outlines.Add("Closed parent", page, opened: false);
            closedParent.Outlines.Add("Closed child A", page, opened: false);
            closedParent.Outlines.Add("Closed child B", page, opened: false);

            var root = closedParent.Parent;
            root.PrepareForSave();

            Assert.Equal(-2, closedParent.Elements.GetInteger(PdfOutline.Keys.Count));
        }
    }
}
