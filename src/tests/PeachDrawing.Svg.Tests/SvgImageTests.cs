using PeachDrawing.Core;
using PeachImage.Formats.Bmp;
using System.Numerics;
using static PeachDrawing.Svg.Tests.SvgScene;

namespace PeachDrawing.Svg.Tests;

public class SvgImageTests
{
    [Fact]
    public void AnImageIsEmbeddedOnceAsItsOwnPngAndEveryDrawIsAUseOfIt()
    {
        using var scene = Create();
        var image = scene.Png(8);
        scene.Canvas.DrawImage(image, new Rect(10, 20, 16, 32));
        scene.Canvas.DrawImage(image, new Rect(50, 0, 8, 8));

        var def = Assert.Single(scene.Defs());
        Assert.Equal(Ns + "image", def.Name);
        Assert.Equal(("8", "8"), (Attr(def, "width"), Attr(def, "height")));
        Assert.StartsWith("data:image/png;base64,", Attr(def, "href"));

        var uses = scene.Drawn().ToList();
        Assert.All(uses, u => Assert.Equal("#" + Attr(def, "id"), Attr(u, "href")));
        Assert.Equal("matrix(2 0 0 4 10 20)", Attr(uses[0], "transform"));
        Assert.Equal("matrix(1 0 0 1 50 0)", Attr(uses[1], "transform"));
    }

    [Fact]
    public void AnImageBrowsersShowIsEmbeddedExactlyAsItCameAndOtherFormatsBecomePng()
    {
        using var scene = Create();
        var raster = new RasterRenderContext();
        using var canvas = raster.CreateCanvas(4, 4);
        canvas.DrawRectangle(raster.GetSolidBrush(PaintColor.FromArgb(255, 0, 128, 0)), 0, 0, 4, 4);

        using var png = new MemoryStream();
        canvas.Save(png, "png", new PeachImage.Formats.Png.PngEncoderOptions());
        var original = png.ToArray();
        scene.Canvas.DrawImage(scene.Context.ImageFromStream(new MemoryStream(original)), new Rect(0, 0, 4, 4));
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(original), Attr(Assert.Single(scene.Defs()), "href"));

        using var bmp = new MemoryStream();
        canvas.Save(bmp, "bmp", new BmpEncoderOptions());
        using var other = Create();
        other.Canvas.DrawImage(other.Context.ImageFromStream(new MemoryStream(bmp.ToArray())), new Rect(0, 0, 4, 4));
        Assert.StartsWith("data:image/png;base64,", Attr(Assert.Single(other.Defs()), "href"));
    }

    [Fact]
    public void ADecodedImageExposesItsPixelsAndSize()
    {
        using var scene = Create();
        var image = scene.Png(6);

        Assert.Equal((6.0, 6.0), (image.Width, image.Height));
        var pixels = image.GetPixels()!.Value;
        Assert.Equal(6 * 6 * 4, pixels.PremultipliedRgba.Length);
        Assert.Equal(200, pixels.PremultipliedRgba.Span[0]);
    }

    [Fact]
    public void ADataStreamThatIsNotAnImageIsRejected()
    {
        using var scene = Create();

        Assert.Throws<InvalidDataException>(() => scene.Context.ImageFromStream(new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8])));
    }

    [Fact]
    public void APartOfAnImageIsShownByClippingTheScaledWholeToTheDestination()
    {
        using var scene = Create();
        var image = scene.Png(8);
        scene.Canvas.DrawImage(image, new Rect(100, 50, 40, 40), new Rect(2, 2, 4, 4));

        var group = Assert.Single(scene.Drawn());
        var clip = Assert.Single(scene.Defs(), e => e.Name == Ns + "clipPath");
        Assert.Equal($"url(#{Attr(clip, "id")})", Attr(group, "clip-path"));
        var rect = Assert.Single(clip.Elements());
        Assert.Equal(("100", "50", "40", "40"), (Attr(rect, "x"), Attr(rect, "y"), Attr(rect, "width"), Attr(rect, "height")));
        Assert.Equal("matrix(10 0 0 10 80 30)", Attr(Assert.Single(group.Elements()), "transform"));
    }

    [Fact]
    public void ASourceRectangleCoveringTheWholeImageNeedsNoClip()
    {
        using var scene = Create();
        scene.Canvas.DrawImage(scene.Png(8), new Rect(0, 0, 8, 8), new Rect(0, 0, 8, 8));

        Assert.Equal(Ns + "use", Assert.Single(scene.Drawn()).Name);
        Assert.DoesNotContain(scene.Defs(), e => e.Name == Ns + "clipPath");
    }

    [Theory]
    [InlineData(ImageSampling.Nearest, "image-rendering:pixelated")]
    [InlineData(ImageSampling.Pixelated, "image-rendering:pixelated")]
    [InlineData(ImageSampling.Bilinear, "")]
    [InlineData(ImageSampling.Automatic, "")]
    public void SamplingDecidesWhetherPixelsAreKeptCrisp(ImageSampling sampling, string expectedStyle)
    {
        using var scene = Create();
        scene.Canvas.DrawImage(scene.Png(), new Rect(0, 0, 40, 40), sampling);

        Assert.Equal(expectedStyle, Attr(Assert.Single(scene.Drawn()), "style"));
    }

    [Fact]
    public void AnImageThatAsksForCrispPixelsIsDrawnCrisp()
    {
        using var scene = Create();
        var image = scene.Png();
        image.Interpolate = false;
        scene.Canvas.DrawImage(image, new Rect(0, 0, 40, 40));

        Assert.Equal("image-rendering:pixelated", Attr(Assert.Single(scene.Drawn()), "style"));
    }

    [Fact]
    public void OpacityAndBlendModeGoOnTheImagesOwnElement()
    {
        using var scene = Create();
        scene.Canvas.DrawImageWithOpacity(scene.Png(), new Rect(0, 0, 8, 8), 0.4, PaintBlendMode.Multiply);

        var use = Assert.Single(scene.Drawn());
        Assert.Equal("0.4", Attr(use, "opacity"));
        Assert.Equal("mix-blend-mode:multiply", Attr(use, "style"));
    }

    [Fact]
    public void ALuminosityMaskIsAnSvgMaskOfTheMaskImageOverTheDestination()
    {
        using var scene = Create();
        scene.Canvas.DrawImageMasked(scene.Png(), scene.Png(4), new Rect(5, 6, 20, 20));

        var mask = Assert.Single(scene.Defs(), e => e.Name == Ns + "mask");
        Assert.Equal(("5", "6", "20", "20", "sRGB"), (Attr(mask, "x"), Attr(mask, "y"), Attr(mask, "width"), Attr(mask, "height"), Attr(mask, "color-interpolation")));
        Assert.Equal(Ns + "use", Assert.Single(mask.Elements()).Name);

        var group = Assert.Single(scene.Drawn());
        Assert.Equal($"url(#{Attr(mask, "id")})", Attr(group, "mask"));
        Assert.Equal(Ns + "use", Assert.Single(group.Elements()).Name);
    }

    [Theory]
    [InlineData(false, "0 0 0 0 1 0 0 0 0 1 0 0 0 0 1 0 0 0 1 0")]
    [InlineData(true, "0 0 0 0 1 0 0 0 0 1 0 0 0 0 1 0 0 0 -1 1")]
    public void AnAlphaMaskTurnsTheMaskImageIntoWhiteOfItsOwnOrItsInverseOpacity(bool invert, string matrix)
    {
        using var scene = Create();
        scene.Canvas.DrawImageAlphaMasked(scene.Png(), scene.Png(4), new Rect(0, 0, 20, 20), invert);

        var filter = Assert.Single(scene.Defs(), e => e.Name == Ns + "filter");
        Assert.Equal(matrix, Attr(Assert.Single(filter.Elements()), "values"));
        var mask = Assert.Single(scene.Defs(), e => e.Name == Ns + "mask");
        Assert.Equal($"url(#{Attr(filter, "id")})", Attr(Assert.Single(mask.Elements()), "filter"));
    }

    [Fact]
    public void AColourMatrixOnAnImageIsAFeColorMatrixConfinedToTheDestination()
    {
        using var scene = Create();
        var grey = new ColorMatrix(new Matrix4x4(0.3f, 0.3f, 0.3f, 0, 0.6f, 0.6f, 0.6f, 0, 0.1f, 0.1f, 0.1f, 0, 0, 0, 0, 1), Vector4.Zero);
        scene.Canvas.DrawImageWithColorMatrix(scene.Png(), new Rect(1, 2, 30, 40), grey);

        var filter = Assert.Single(scene.Defs(), e => e.Name == Ns + "filter");
        Assert.Equal(("1", "2", "30", "40"), (Attr(filter, "x"), Attr(filter, "y"), Attr(filter, "width"), Attr(filter, "height")));
        Assert.Equal("0.3 0.6 0.1 0 0 0.3 0.6 0.1 0 0 0.3 0.6 0.1 0 0 0 0 0 1 0", Attr(Assert.Single(filter.Elements()), "values"));
        Assert.Equal($"url(#{Attr(filter, "id")})", Attr(Assert.Single(scene.Drawn()), "filter"));
    }

    [Fact]
    public void ABlendedPairIsAnIsolatedGroupOfTheBottomImageAndTheTopOneBlendedOverIt()
    {
        using var scene = Create();
        scene.Canvas.DrawImageBlendedOver(scene.Png(), scene.Png(4), new Rect(0, 0, 8, 8), PaintBlendMode.Difference);

        var group = Assert.Single(scene.Drawn());
        Assert.Equal("isolation:isolate", Attr(group, "style"));
        var inner = group.Elements().ToList();
        Assert.Equal(2, inner.Count);
        Assert.Equal(string.Empty, Attr(inner[0], "style"));
        Assert.Equal("mix-blend-mode:difference", Attr(inner[1], "style"));
    }

    [Fact]
    public void AnImageFromAnotherBackendIsConvertedToPngFromItsPixels()
    {
        using var scene = Create();
        var raster = new RasterRenderContext();
        using var rasterCanvas = raster.CreateCanvas(4, 4);
        var tile = rasterCanvas.CreateTile(4, 4)!.Value;
        tile.Graphics.DrawRectangle(raster.GetSolidBrush(PaintColor.FromArgb(255, 1, 2, 3)), 0, 0, 4, 4);

        scene.Canvas.DrawImage(tile.Image, new Rect(0, 0, 4, 4));
        scene.Canvas.DrawImage(tile.Image, new Rect(4, 0, 4, 4));

        Assert.StartsWith("data:image/png;base64,", Attr(Assert.Single(scene.Defs()), "href"));
    }

    [Fact]
    public void EmptyRectanglesAndDegenerateSourcesDrawNothing()
    {
        using var scene = Create();
        scene.Canvas.DrawImage(scene.Png(), new Rect(0, 0, 0, 10));
        scene.Canvas.DrawImage(scene.Png(), new Rect(0, 0, 10, 10), new Rect(0, 0, 0, 4));

        Assert.Empty(scene.Drawn());
    }

    [Fact]
    public void ARasterRegionIsEmbeddedAsPngWhereItCoversAndIsTheOnlyNonVectorPath()
    {
        using var scene = Create(100, 100);
        using var region = scene.Canvas.BeginRasterSurface(new Rect(10, 20, 30, 40));
        Assert.NotNull(region);
        region!.Graphics.DrawRectangle(scene.Solid(255, 0, 0), 10, 20, 30, 40);
        scene.Canvas.DrawRaster(region.Surface);

        var image = Assert.Single(scene.Drawn());
        Assert.Equal(Ns + "image", image.Name);
        // The surface snaps outwards to its pixel grid, so it covers the region asked for.
        var (x, y, w, h) = (double.Parse(Attr(image, "x")), double.Parse(Attr(image, "y")), double.Parse(Attr(image, "width")), double.Parse(Attr(image, "height")));
        Assert.True(x <= 10 && y <= 20 && x + w >= 40 && y + h >= 60);
        Assert.StartsWith("data:image/png;base64,", Attr(image, "href"));
    }

    [Fact]
    public void ARasterRegionOfNothingIsNull()
    {
        using var scene = Create();

        Assert.Null(scene.Canvas.BeginRasterSurface(new Rect(0, 0, 0, 0)));
    }
}
