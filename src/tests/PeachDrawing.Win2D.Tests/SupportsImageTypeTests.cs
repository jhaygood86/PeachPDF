using PeachDrawing.Win2D;

namespace PeachDrawing.Win2D.Tests;

/// <summary>Which image media types a Win2D context says it can decode.</summary>
public class SupportsImageTypeTests
{
    [Theory]
    [InlineData(Win2dImageCodec.Auto, "image/avif")]
    [InlineData(Win2dImageCodec.PeachImage, "image/jxl")]
    [InlineData(Win2dImageCodec.Native, "image/webp")]
    public void WithPeachImageBehind_EverythingItReadsIsSupported(Win2dImageCodec codec, string type)
    {
        using var context = Win2dRenderContext.CreateSoftware(imageCodec: codec);

        Assert.True(context.SupportsImageType(type));
    }

    [Theory]
    [InlineData("image/png", true)]
    [InlineData("image/tiff", true)]
    [InlineData("image/svg+xml", true)]
    [InlineData("image/avif", false)]
    [InlineData("image/jxl", false)]
    [InlineData("text/plain", false)]
    public void NativeCodecWithoutFallback_IsWhatWindowsReads(string type, bool expected)
    {
        using var context = Win2dRenderContext.CreateSoftware(imageCodec: Win2dImageCodec.Native);
        context.FallbackToPeachImage = false;

        Assert.Equal(expected, context.SupportsImageType(type));
    }
}
