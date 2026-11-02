using PeachBrackets.WinUI;

namespace PeachBrackets.WinUI.Tests;

public class CursorImagesTests
{
    [Theory]
    [InlineData("data:image/png;base64,AAAA", true)]
    [InlineData("DATA:image/png;base64,AAAA", true)]
    [InlineData("https://example.com/c.png", true)]
    [InlineData("http://example.com/c.png", true)]
    [InlineData("file:///C:/c.cur", true)]
    [InlineData("ms-appx:///c.png", false)]
    [InlineData("c.png", false)]
    public void OnlyPlainAddressesAreFetched(string uri, bool expected) =>
        Assert.Equal(expected, CursorImages.IsFetchable(uri));

    [Theory]
    [InlineData(32, 32, 128, 32, 32)]
    [InlineData(256, 128, 128, 128, 64)]
    [InlineData(100, 400, 128, 32, 128)]
    [InlineData(0, 0, 128, 1, 1)]
    public void ImagesAreScaledToFit(int width, int height, int max, int expectedWidth, int expectedHeight) =>
        Assert.Equal((expectedWidth, expectedHeight), CursorImages.Fit(width, height, max));

    [Fact]
    public void TheHotspotMovesWithTheScaling()
    {
        Assert.Equal((8, 8), CursorImages.Hotspot(8, 8, 32, 32, 32, 32));
        Assert.Equal((32, 16), CursorImages.Hotspot(64, 32, 256, 128, 128, 64));
    }

    [Fact]
    public void TheHotspotDefaultsToTheCentreAndStaysInsideThePointer()
    {
        Assert.Equal((16, 16), CursorImages.Hotspot(null, null, 32, 32, 32, 32));
        Assert.Equal((31, 0), CursorImages.Hotspot(500, -4, 32, 32, 32, 32));
    }

    [Fact]
    public void ACursorFileCarriesItsHotspot()
    {
        var file = new byte[22];
        file[2] = 2;   // type: cursor
        file[4] = 1;   // one image
        file[10] = 5;  // hotspot x
        file[12] = 9;  // hotspot y
        Assert.Equal((5, 9), CursorImages.CurHotspot(file));
    }

    [Fact]
    public void OtherBytesHaveNoHotspot()
    {
        Assert.Null(CursorImages.CurHotspot(new byte[22]));
        Assert.Null(CursorImages.CurHotspot(new byte[4]));
        var icon = new byte[22];
        icon[2] = 1;   // type: icon
        icon[4] = 1;
        Assert.Null(CursorImages.CurHotspot(icon));
    }

    [Fact]
    public void DataAddressesAreDecoded()
    {
        Assert.True(CursorImages.TryDecodeDataUri("data:image/png;base64,AQID", out var base64));
        Assert.Equal(new byte[] { 1, 2, 3 }, base64);
        Assert.True(CursorImages.TryDecodeDataUri("data:,%01%02", out var plain));
        Assert.Equal(new byte[] { 1, 2 }, plain);
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("data:image/png;base64")]
    [InlineData("data:image/png;base64,!!!")]
    public void BadDataAddressesAreRefused(string uri) =>
        Assert.False(CursorImages.TryDecodeDataUri(uri, out _));
}
