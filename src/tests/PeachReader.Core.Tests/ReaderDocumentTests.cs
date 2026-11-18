using PeachDrawing.Core;
using PeachDrawing.PDF;

namespace PeachReader.Core.Tests;

public class ReaderDocumentTests
{
    [Fact]
    public void Open_ReadsPagesTheirSizesAndTheTitle()
    {
        var doc = ReaderDocument.Open(TestDocument.Sample("The Peach Report"), fileName: "report.pdf");

        Assert.Equal(3, doc.PageCount);
        Assert.Equal("The Peach Report", doc.Title);
        Assert.Equal((300d, 200d), doc.PageSizes[0]);
        Assert.Equal((200d, 300d), doc.PageSizes[2]);
        Assert.Equal(64, doc.Key.Length);
    }

    [Fact]
    public void Open_TitlesADocumentWithNoneByItsFileName()
    {
        Assert.Equal("annual-report", ReaderDocument.Open(TestDocument.Sample(null), fileName: @"C:\docs\annual-report.pdf").Title);
        Assert.Equal("Untitled", ReaderDocument.Open(TestDocument.Sample(null)).Title);
    }

    [Fact]
    public void Open_GivesTheSameKeyToTheSameBytesAndAnotherToOthers()
    {
        var bytes = TestDocument.Sample();

        Assert.Equal(ReaderDocument.Open(bytes, fileName: "a.pdf").Key, ReaderDocument.Open(bytes, fileName: "b.pdf").Key);
        Assert.NotEqual(ReaderDocument.Open(bytes).Key, ReaderDocument.Open(TestDocument.Sample("Other")).Key);
    }

    [Fact]
    public void Open_RejectsWhatIsNotAPdf()
    {
        Assert.Throws<InvalidDataException>(() => ReaderDocument.Open([1, 2, 3]));
        Assert.Throws<ArgumentNullException>(() => ReaderDocument.Open(null!));
    }

    [Fact]
    public void Outline_IsATreeWithTheDestinationsOfItsEntries()
    {
        var doc = ReaderDocument.Open(TestDocument.Sample());

        var roots = doc.Outline;

        Assert.Equal(["Introduction", "Methods", "Results"], roots.Select(r => r.Title));
        var background = Assert.Single(roots[0].Children);
        Assert.Equal("Background", background.Title);
        Assert.Same(roots[0], background.Parent);
        Assert.Equal(new ReaderLocation(0, 100), background.Location);
        Assert.Equal(new ReaderLocation(2, 40), roots[2].Location);
        Assert.Same(doc.Outline, doc.Outline);
    }

    [Fact]
    public void Links_AreReadOncePerPage_AndResolveThroughTheResolver()
    {
        var doc = ReaderDocument.Open(TestDocument.Sample());

        var links = doc.GetLinks(0);

        Assert.Equal(2, links.Count);
        Assert.Same(links, doc.GetLinks(0));
        // The web link is over y 160-175 of a 200 high page, which is 25-40 from the top.
        Assert.Equal(LinkKind.External, LinkResolver.Hit(links, new PaintPoint(50, 30)).Kind);
        var internalLink = LinkResolver.Hit(links, new PaintPoint(50, 90));
        Assert.Equal(LinkKind.Internal, internalLink.Kind);
        Assert.Equal(new ReaderLocation(2, 50), internalLink.Location);
        Assert.Equal(LinkKind.None, LinkResolver.Hit(links, new PaintPoint(250, 190)).Kind);
    }

    [Fact]
    public void Text_IsReadOnceAndSearchable()
    {
        var doc = ReaderDocument.Open(TestDocument.Sample());

        var text = doc.GetText(0);

        Assert.Same(text, doc.GetText(0));
        Assert.Contains("Introduction to peaches", text.Text);
        Assert.Equal("Methods and cherries", doc.GetText(1).Text);
    }

    [Fact]
    public void Text_StaysBoundedForALongDocument()
    {
        var pages = Enumerable.Range(0, 60).Select(i => new TestPage(100, 100, TestDocument.Show("page " + i, height: 100))).ToList();
        var doc = ReaderDocument.Open(TestDocument.Build(pages));

        for (int i = 0; i < 60; i++) Assert.Equal("page " + i, doc.GetText(i).Text);

        // The oldest pages were dropped and are read again; the answer is the same.
        Assert.Equal("page 0", doc.GetText(0).Text);
    }

    [Fact]
    public void ANamedDestinationThatDoesNotExist_ResolvesToNothing()
    {
        Assert.Null(ReaderDocument.Open(TestDocument.Sample()).ResolveNamedDestination("nowhere"));
    }

    [Fact]
    public void PageNumbersOutsideTheDocument_Throw()
    {
        var doc = ReaderDocument.Open(TestDocument.Sample());

        Assert.Throws<ArgumentOutOfRangeException>(() => doc.GetLinks(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.GetText(-1));
    }
}
