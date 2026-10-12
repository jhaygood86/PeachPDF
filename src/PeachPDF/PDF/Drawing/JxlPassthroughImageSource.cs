using PeachPDF.PDF.Drawing;
using PeachImage;
using PeachImage.Formats.Jxl;
using PeachImage.Formats.Bmp;
using PeachImage.Formats.Gif;
using PeachImage.Formats.Jpeg;
using PeachImage.Formats.Png;
using PeachPDF.PdfBackend;
using System;
using System.Collections.Generic;
using System.IO;

using static PeachPDF.PDF.Drawing.ImageSource;

namespace PeachPDF.PDF.Drawing;

/// <summary>
/// A PNG source eligible for byte-for-byte pass-through (see <see cref="PeachImageSource.DecodePng"/>/
/// <see cref="PeachImageSource.IsPassthroughEligible"/>). Unlike <see cref="CmykRasterImageSource"/> - which
/// genuinely has no fallback (PeachImage has no CMYK JPEG encoder) - <see cref="SaveAsJpeg"/>/
/// <see cref="SaveAsPdfBitmap"/> aren't unreachable here: <see cref="PeachPDF.ImageCompression.Lossy"/>
/// explicitly asks for a lossy JPEG re-encode even of an otherwise pass-through-eligible source,
/// and <c>PdfImageTable</c>'s resize skip only applies when compression isn't <c>Lossy</c> (see
/// its own remarks), so a resize can legitimately reach here too under that mode. The full pixel
/// decode is deferred until one of those is actually called rather than paid unconditionally -
/// under the default <see cref="PeachPDF.ImageCompression.Auto"/>/<see cref="PeachPDF.ImageCompression.Lossless"/>
/// modes, neither ever is, so this is genuinely cheaper than today for the common case.
/// </summary>
/// <summary>
/// A decoded JPEG XL source plus the original file bytes, for the experimental <c>/JXLDecode</c> embed. Delegates
/// every member to <see cref="_inner"/> (the same source <c>DecodeJxl</c> returned before this wrapper existed),
/// so a render that does not enable the passthrough is byte-identical to one without it.
/// </summary>
internal sealed class JxlPassthroughImageSource : IImageSource, IRgbaPixelProvider
{
    private readonly IImageSource _inner;
    private readonly JxlPassthroughData _passthrough;

    public JxlPassthroughImageSource(IImageSource inner, JxlPassthroughData passthrough)
    {
        _inner = inner;
        _passthrough = passthrough;
    }

    public int Width => _inner.Width;
    public int Height => _inner.Height;
    public string Name => _inner.Name;
    public bool Transparent => _inner.Transparent;
    public bool IsGrayscale => _inner.IsGrayscale;
    public bool IsCmyk => _inner.IsCmyk;
    public int ExifOrientation => _inner.ExifOrientation;
    public JpegPassthroughData? JpegPassthrough => _inner.JpegPassthrough;
    public CmykRasterData? CmykRaster => _inner.CmykRaster;
    public PngPassthroughData? PngPassthrough => _inner.PngPassthrough;
    public GifPassthroughData? GifPassthrough => _inner.GifPassthrough;
    public bool IsLosslessSourceFormat => _inner.IsLosslessSourceFormat;
    public byte[]? RgbIccProfile => _inner.RgbIccProfile;
    public JxlPassthroughData? JxlPassthrough => _passthrough;

    public void SaveAsJpeg(MemoryStream ms, int? targetWidth = null, int? targetHeight = null, int? qualityOverride = null) =>
        _inner.SaveAsJpeg(ms, targetWidth, targetHeight, qualityOverride);

    public void SaveAsPdfBitmap(MemoryStream ms, int? targetWidth = null, int? targetHeight = null) =>
        _inner.SaveAsPdfBitmap(ms, targetWidth, targetHeight);

    public bool TryGetRgba(out int width, out int height, out byte[] rgba)
    {
        if (_inner is IRgbaPixelProvider provider)
        {
            return provider.TryGetRgba(out width, out height, out rgba);
        }

        width = height = 0;
        rgba = [];
        return false;
    }
}
