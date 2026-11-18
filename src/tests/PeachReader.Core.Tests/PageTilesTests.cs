namespace PeachReader.Core.Tests;

public class PageTilesTests
{
    [Fact]
    public void ASmallPage_IsOneTileOfItsOwnSize()
    {
        var tile = Assert.Single(PageTiles.Visible(600.2, 800, 0, 0, 1000, 1000));

        Assert.Equal(new PageTile(0, 0, 0, 0, 601, 800), tile);
    }

    [Fact]
    public void ALargePage_IsCutIntoTilesThatMeetAtTheEdges()
    {
        var tiles = PageTiles.Visible(2500, 1500, 0, 0, 2500, 1500);

        Assert.Equal(6, tiles.Count);                                            // three columns by two rows
        Assert.Equal(new PageTile(0, 0, 0, 0, 1024, 1024), tiles[0]);
        Assert.Equal(new PageTile(2, 0, 2048, 0, 452, 1024), tiles[2]);          // the right edge is narrower
        Assert.Equal(new PageTile(1, 1, 1024, 1024, 1024, 476), tiles[4]);       // and the bottom edge shorter
        Assert.Equal(2500, tiles.Where(t => t.Row == 0).Sum(t => t.Width));
    }

    [Fact]
    public void OnlyTheTilesTheWindowTouchesAreListed()
    {
        var tiles = PageTiles.Visible(4000, 4000, 1500, 2100, 2500, 2200);

        Assert.Equal([(1, 2), (2, 2)], tiles.Select(t => (t.Column, t.Row)));
    }

    [Fact]
    public void AWindowThatEndsExactlyOnATileEdgeDoesNotTouchTheNextTile()
    {
        var tiles = PageTiles.Visible(4000, 4000, 0, 0, 1024, 1024);

        Assert.Single(tiles);
    }

    [Theory]
    [InlineData(-500, -500, -10, -10)]       // wholly before the page
    [InlineData(5000, 0, 6000, 100)]         // wholly beyond it
    [InlineData(10, 10, 10, 50)]             // empty
    public void AWindowOffThePage_TouchesNothing(double left, double top, double right, double bottom)
    {
        Assert.Empty(PageTiles.Visible(1000, 1000, left, top, right, bottom));
    }

    [Fact]
    public void ADegeneratePage_HasNoTiles_AndRowCountIsAtLeastOne()
    {
        Assert.Empty(PageTiles.Visible(0, 100, 0, 0, 10, 10));
        Assert.Equal(1, PageTiles.RowCount(0));
        Assert.Equal(1, PageTiles.RowCount(1024));
        Assert.Equal(2, PageTiles.RowCount(1025));
    }

    [Fact]
    public void AWindowPartlyBeforeThePage_StartsAtTheFirstTile()
    {
        var tiles = PageTiles.Visible(3000, 3000, -200, -200, 500, 500);

        Assert.Equal(new PageTile(0, 0, 0, 0, 1024, 1024), Assert.Single(tiles));
    }
}
