using PeachReader.Core;

namespace PeachReader.Core.Tests;

public class PageLabelTests
{
    private static ReaderDocument Open() => ReaderDocument.Open(TestDocument.Build(
        [.. Enumerable.Range(0, 5).Select(i => new TestPage(200, 200, TestDocument.Show("p" + i)))],
        catalogExtras: "/PageLabels << /Nums [0 << /S /r >> 2 << /S /D /P (A-) >>] >>"));

    [Fact]
    public void EachPageHasItsLabel()
    {
        var doc = Open();

        Assert.Equal(["i", "ii", "A-1", "A-2", "A-3"], doc.PageLabels);
        Assert.Equal("A-2", doc.LabelOf(3));
    }

    [Theory]
    [InlineData("ii", 1)]
    [InlineData("a-3", 4)]
    [InlineData(" III ", -1)]
    [InlineData("4", 3)]
    [InlineData("9", -1)]
    [InlineData("", -1)]
    [InlineData("zz", -1)]
    public void ATypedPageIsFoundByLabelThenByNumber(string typed, int expected)
    {
        Assert.Equal(expected, Open().PageOf(typed));
    }
}
