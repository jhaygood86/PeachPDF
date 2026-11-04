using PeachDrawing.Skia;

namespace PeachDrawing.Skia.Tests;

/// <summary>Which image media types a Skia context says it can decode.</summary>
public class SupportsImageTypeTests
{
    [Theory]
    [InlineData(SkiaImageCodec.Auto, "image/avif", true)]
    [InlineData(SkiaImageCodec.PeachImage, "image/jxl", true)]
    [InlineData(SkiaImageCodec.Native, "image/avif", true)]
    [InlineData(SkiaImageCodec.Native, "image/webp", true)]
    public void WithPeachImageBehind_EverythingItReadsIsSupported(SkiaImageCodec codec, string type, bool expected)
    {
        using var context = new SkiaRenderContext(imageCodec: codec);

        Assert.Equal(expected, context.SupportsImageType(type));
    }

    [Theory]
    [InlineData("image/png", true)]
    [InlineData("image/webp", true)]
    [InlineData("image/svg+xml", true)]
    [InlineData("image/avif", false)]
    [InlineData("image/jxl", false)]
    [InlineData("image/tiff", false)]
    [InlineData("text/plain", false)]
    public void NativeCodecWithoutFallback_IsWhatSkiaReads(string type, bool expected)
    {
        using var context = new SkiaRenderContext(imageCodec: SkiaImageCodec.Native) { FallbackToPeachImage = false };

        Assert.Equal(expected, context.SupportsImageType(type));
    }
}
