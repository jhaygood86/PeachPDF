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

internal sealed class PngPassthroughImageSource : IImageSource, IRgbaPixelProvider
{
    private readonly byte[] _bytes;
    private readonly int _quality;
    private readonly PngPassthroughData _passthrough;
    private DecodedImageSource? _decodedFallback;

    public int Width { get; }
    public int Height { get; }
    public string Name { get; }
    public bool Transparent => false;
    public bool IsCmyk => false;
    public int ExifOrientation { get; set; } = 1;
    public bool IsGrayscale => false;
    public JpegPassthroughData? JpegPassthrough => null;
    public CmykRasterData? CmykRaster => null;
    public PngPassthroughData? PngPassthrough => _passthrough;
    public GifPassthroughData? GifPassthrough => null;
    public bool IsLosslessSourceFormat => true;

    // Read by InitializeJpeg's ImageCompression.Lossy fallback (issue #1106): when Lossy forces
    // this source through DecodedFallback's JPEG re-encode instead of EmbedPngPassthrough, the
    // re-encoded stream's samples are still the same ones the source's own iCCP profile
    // describes (a JPEG re-encode recompresses pixel values, it doesn't reinterpret them), so
    // the profile the pass-through path already parsed carries over rather than being dropped.
    public byte[]? RgbIccProfile => _passthrough.IccProfile;

    public PngPassthroughImageSource(string name, byte[] bytes, int quality, int width, int height, PngPassthroughData passthrough)
    {
        Name = name;
        _bytes = bytes;
        _quality = quality;
        Width = width;
        Height = height;
        _passthrough = passthrough;
    }

    // Forces Rgba32 the same as any other non-JPEG format's generic decode (Decode's own
    // PeachImageSource.Rgba32DecoderOptions) - IsGrayscale stays false for this fallback regardless of the source's
    // own PNG color type, which is fine: unlike the JPEG grayscale bug this mirrors the shape of,
    // decode and re-encode are consistently RGB here, so no /ColorSpace-vs-stream mismatch results,
    // just a 3-component JPEG for what could have been a 1-component one - an acceptable size
    // trade for what's already an explicit Lossy opt-out of the lossless default.
    private DecodedImageSource DecodedFallback() =>
        _decodedFallback ??= new DecodedImageSource(Name, Image.Load(new MemoryStream(_bytes), PeachImageSource.Rgba32DecoderOptions), _quality, transparent: false);

    public void SaveAsJpeg(MemoryStream ms, int? targetWidth = null, int? targetHeight = null, int? qualityOverride = null) =>
        DecodedFallback().SaveAsJpeg(ms, targetWidth, targetHeight, qualityOverride);

    public void SaveAsPdfBitmap(MemoryStream ms, int? targetWidth = null, int? targetHeight = null) =>
        DecodedFallback().SaveAsPdfBitmap(ms, targetWidth, targetHeight);

    public bool TryGetRgba(out int width, out int height, out byte[] rgba) =>
        DecodedFallback().TryGetRgba(out width, out height, out rgba);
}
