using PeachDrawing.Abstractions;
using PeachImage;
using System;

namespace PeachDrawing;

/// <summary>
/// A <see cref="PeachDrawing.Abstractions.Image"/> decoded directly through <c>PeachImage</c> - the portable decode library
/// PeachPDF's own <c>PeachImageSource</c> already builds on - with no PdfSharpCore type (no
/// <c>PeachPDF.PdfSharpCore.Drawing.XImage</c>) anywhere in the chain. This is what
/// <see cref="RasterRenderContext.ImageFromStreamInt"/> uses: unlike <c>XImage</c>, which conflates
/// decoding with PDF-embedding concerns (lazy JPEG/CMYK pass-through for direct PDF stream embedding,
/// <c>IsRasterOutput</c> flagging, ...), a standalone raster canvas only ever wants this image's pixels to
/// sample, so it decodes eagerly and keeps nothing PDF-specific. This type lives in this package rather
/// than <c>PeachDrawing.Abstractions</c> because it depends on <c>PeachImage</c> for the decode itself, a
/// dependency the abstraction layer deliberately does not take (a different <c>Canvas</c> backend may
/// decode images an entirely different way).
/// </summary>
internal sealed class DecodedImage : PeachDrawing.Abstractions.Image
{
    private readonly PixelBuffer _pixels;

    private DecodedImage(int width, int height, PixelBuffer pixels)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
    }

    /// <summary>Decodes <paramref name="stream"/> to premultiplied RGBA8, or null when PeachImage cannot recognise the format.</summary>
    public static DecodedImage? TryDecode(System.IO.Stream stream)
    {
        if (!PeachImage.Image.TryLoad(stream, out var image, new DecoderOptions { TargetPixelFormat = PixelFormat.Rgba32 }) || image is null)
            return null;

        using (image)
        {
            var rgba = image.PixelMemory.Span.ToArray();
            Bitmap.Premultiply(rgba);
            return new DecodedImage(image.Width, image.Height, new PixelBuffer(image.Width, image.Height, rgba));
        }
    }

    public override double Width { get; }

    public override double Height { get; }

    public override bool Interpolate { get; set; } = true;

    public override void Dispose()
    {
    }

    public override PixelBuffer? GetPixels() => _pixels;
}
