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
/// A GIF source eligible for <c>/LZWDecode</c> pass-through (see <see cref="PeachImageSource.DecodeGif"/>/
/// <see cref="PeachImageSource.IsGifPassthroughEligible"/>) - same shape as
/// <see cref="PngPassthroughImageSource"/>, including its <see cref="ImageCompression.Lossy"/>
/// decoded-fallback reasoning.
/// </summary>
internal sealed class GifPassthroughImageSource : IImageSource, IRgbaPixelProvider
{
    private readonly byte[] _bytes;
    private readonly int _quality;
    private readonly GifPassthroughData _passthrough;
    private DecodedImageSource? _decodedFallback;

    public int Width { get; }
    public int Height { get; }
    public string Name { get; }
    public bool Transparent => false;
    public bool IsCmyk => false;
    public bool IsGrayscale => false;
    public JpegPassthroughData? JpegPassthrough => null;
    public CmykRasterData? CmykRaster => null;
    public PngPassthroughData? PngPassthrough => null;
    public GifPassthroughData? GifPassthrough => _passthrough;
    public bool IsLosslessSourceFormat => true;
    public byte[]? RgbIccProfile => null;

    public GifPassthroughImageSource(string name, byte[] bytes, int quality, int width, int height, GifPassthroughData passthrough)
    {
        Name = name;
        _bytes = bytes;
        _quality = quality;
        Width = width;
        Height = height;
        _passthrough = passthrough;
    }

    private DecodedImageSource DecodedFallback() =>
        _decodedFallback ??= new DecodedImageSource(Name, Image.Load(new MemoryStream(_bytes), PeachImageSource.Rgba32DecoderOptions), _quality, transparent: false);

    public void SaveAsJpeg(MemoryStream ms, int? targetWidth = null, int? targetHeight = null, int? qualityOverride = null) =>
        DecodedFallback().SaveAsJpeg(ms, targetWidth, targetHeight, qualityOverride);

    public void SaveAsPdfBitmap(MemoryStream ms, int? targetWidth = null, int? targetHeight = null) =>
        DecodedFallback().SaveAsPdfBitmap(ms, targetWidth, targetHeight);

    public bool TryGetRgba(out int width, out int height, out byte[] rgba) =>
        DecodedFallback().TryGetRgba(out width, out height, out rgba);
}
