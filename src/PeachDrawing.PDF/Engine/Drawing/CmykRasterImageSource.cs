using PeachDrawing.PDF.Engine.Drawing;
using PeachImage;
using PeachImage.Formats.Jxl;
using PeachImage.Formats.Bmp;
using PeachImage.Formats.Gif;
using PeachImage.Formats.Jpeg;
using PeachImage.Formats.Png;
using PeachDrawing.PDF;
using System;
using System.Collections.Generic;
using System.IO;

using static PeachDrawing.PDF.Engine.Drawing.ImageSource;

namespace PeachDrawing.PDF.Engine.Drawing;

/// <summary>
/// Embeds a CMYK source - either JPEG (via <see cref="JpegPassthroughData"/>, the original file
/// bytes, never resized or re-encoded) or TIFF (via <see cref="CmykRasterData"/>, a raw decoded
/// pixel buffer - see <see cref="ImageSource.IImageSource.CmykRaster"/>'s own remarks on why
/// TIFF needs a different shape). Exactly one of the two constructors is ever used for a given
/// instance - see <c>PdfImageTable.ComputeTargetPixelSize</c>'s CMYK resize skip, which applies to
/// both: PeachImage has no CMYK JPEG encoder, and a raw CMYK raster embed has no resize step
/// either, so a CMYK source is never resized regardless of which shape it took.
/// </summary>
internal sealed class CmykRasterImageSource : IImageSource, IRgbaPixelProvider
{
    private readonly JpegPassthroughData? _passthrough;
    private readonly CmykRasterData? _raster;

    public int Width { get; }
    public int Height { get; }
    public string Name { get; }
    public bool Transparent => false;
    public bool IsCmyk => true;
    public int ExifOrientation { get; set; } = 1;
    public bool IsGrayscale => false;
    public JpegPassthroughData? JpegPassthrough => _passthrough;
    public CmykRasterData? CmykRaster => _raster;
    public PngPassthroughData? PngPassthrough => null;
    public GifPassthroughData? GifPassthrough => null;
    public bool IsLosslessSourceFormat => false;
    public byte[]? RgbIccProfile => null;

    public CmykRasterImageSource(string name, int width, int height, JpegPassthroughData passthrough)
    {
        Name = name;
        Width = width;
        Height = height;
        _passthrough = passthrough;
    }

    public CmykRasterImageSource(string name, int width, int height, CmykRasterData raster)
    {
        Name = name;
        Width = width;
        Height = height;
        _raster = raster;
    }

    public void SaveAsJpeg(MemoryStream ms, int? targetWidth = null, int? targetHeight = null, int? qualityOverride = null) =>
        throw new InvalidOperationException("A CMYK image is always embedded via JpegPassthrough or CmykRaster; SaveAsJpeg is never called for it.");

    public void SaveAsPdfBitmap(MemoryStream ms, int? targetWidth = null, int? targetHeight = null) =>
        throw new InvalidOperationException("A CMYK image is always embedded via JpegPassthrough or CmykRaster; SaveAsPdfBitmap is never called for it.");

    // The PDF embed keeps CMYK exactly as authored, but a bitmap rendition of it has no colour management to keep: the
    // usual naive conversion (r = (1 - c)(1 - k) ...) is what any raster fallback for CMYK does.
    public bool TryGetRgba(out int width, out int height, out byte[] rgba)
    {
        width = Width;
        height = Height;

        if (_raster is { } raster)
        {
            rgba = new byte[width * height * 4];
            for (int i = 0, j = 0; i < rgba.Length && j + 3 < raster.Data.Length; i += 4, j += 4)
            {
                int k = 255 - raster.Data[j + 3];
                rgba[i] = (byte)((255 - raster.Data[j]) * k / 255);
                rgba[i + 1] = (byte)((255 - raster.Data[j + 1]) * k / 255);
                rgba[i + 2] = (byte)((255 - raster.Data[j + 2]) * k / 255);
                rgba[i + 3] = 255;
            }

            return true;
        }

        if (_passthrough is { } jpeg)
        {
            // A CMYK JPEG: let the codec convert it to RGBA, as it does for every other source.
            using var decoded = Image.Load(new MemoryStream(jpeg.Data), PeachImageSource.Rgba32DecoderOptions);
            rgba = decoded.GetPixelSpan().Slice(0, width * height * 4).ToArray();
            return true;
        }

        rgba = [];
        return false;
    }
}
