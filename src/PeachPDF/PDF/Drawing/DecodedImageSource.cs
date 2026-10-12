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

internal sealed class DecodedImageSource : IImageSource, IRgbaPixelProvider
{
    private readonly Image _rgba;
    private readonly int _quality;
    private readonly JpegPassthroughData? _jpegPassthrough;

    public int Width => _rgba.Width;
    public int Height => _rgba.Height;
    public string Name { get; }
    public bool Transparent { get; }
    public bool IsCmyk => false;
    public int ExifOrientation { get; set; } = 1;

    // Only ever true for a JPEG source decoded via DecodeRgbOrGrayJpeg's native (not
    // Rgba32-forced) path - every other format still forces Rgba32 (see Rgba32DecoderOptions'
    // own remarks), so _rgba.PixelFormat is never Gray8 for those. Matters because SaveAsJpeg's
    // re-encode genuinely emits a 1-component grayscale JPEG for a Gray8 source (PeachImage's
    // FrameEncoder branches on the source's own PixelFormat), and PdfImage's resize-fallback path
    // needs to write a matching /ColorSpace for that - see IImageSource.IsGrayscale's own remarks.
    public bool IsGrayscale => _rgba.PixelFormat == PixelFormat.Gray8;
    public JpegPassthroughData? JpegPassthrough => _jpegPassthrough;
    public CmykRasterData? CmykRaster => null;
    public PngPassthroughData? PngPassthrough => null;
    public GifPassthroughData? GifPassthrough => null;

    // True for BMP/GIF and for a PNG that decoded here because it wasn't pass-through-eligible
    // (see DecodePng) - both callers pass this explicitly; JPEG (DecodeRgbOrGrayJpeg) leaves it at
    // its false default, since JPEG always has a lossy encoding mode.
    public bool IsLosslessSourceFormat { get; }

    // Set only by DecodeGenericRaster for a WebP/AVIF source with a usable embedded ICC profile
    // (issue #1106) - every other caller leaves this at its null default. See RgbIccProfile's own
    // interface-level remarks for why this is the only format pair that ever populates it.
    public byte[]? RgbIccProfile { get; }

    public DecodedImageSource(string name, Image rgba, int quality, bool transparent, JpegPassthroughData? jpegPassthrough = null, bool isLosslessSourceFormat = false, byte[]? rgbIccProfile = null)
    {
        Name = name;
        _rgba = rgba;
        _quality = quality;
        Transparent = transparent;
        _jpegPassthrough = jpegPassthrough;
        IsLosslessSourceFormat = isLosslessSourceFormat;
        RgbIccProfile = rgbIccProfile;
    }

    public void SaveAsJpeg(MemoryStream ms, int? targetWidth = null, int? targetHeight = null, int? qualityOverride = null)
    {
        // JPEG ignores the alpha channel of a Rgba32 source.
        using var scope = new ResizeScope(_rgba, targetWidth, targetHeight);
        scope.Source.Save(ms, "jpeg", new JpegEncoderOptions { Quality = qualityOverride ?? _quality });
    }

    public void SaveAsPdfBitmap(MemoryStream ms, int? targetWidth = null, int? targetHeight = null)
    {
        using var scope = new ResizeScope(_rgba, targetWidth, targetHeight);
        scope.Source.Save(ms, "bmp", new BmpEncoderOptions());
    }

    // Resolves to either the original decoded image or a resized clone, so SaveAsJpeg/
    // SaveAsPdfBitmap share one resize decision instead of duplicating it. Disposing a scope
    // that didn't resize is a no-op - it never owns _rgba.
    private readonly struct ResizeScope : IDisposable
    {
        public readonly Image Source;
        private readonly bool _owned;

        public ResizeScope(Image rgba, int? targetWidth, int? targetHeight)
        {
            if (targetWidth is int w && targetHeight is int h && (w != rgba.Width || h != rgba.Height))
            {
                Source = rgba.Resize(w, h, new ResizeOptions { Filter = ResamplingFilter.Bicubic });
                _owned = true;
            }
            else
            {
                Source = rgba;
                _owned = false;
            }
        }

        public void Dispose()
        {
            if (_owned) Source.Dispose();
        }
    }

    public bool TryGetRgba(out int width, out int height, out byte[] rgba)
    {
        width = _rgba.Width;
        height = _rgba.Height;
        var pixels = _rgba.GetPixelSpan();
        rgba = new byte[width * height * 4];

        switch (_rgba.PixelFormat)
        {
            case PixelFormat.Rgba32:
                pixels.Slice(0, rgba.Length).CopyTo(rgba);
                return true;

            case PixelFormat.Rgb24:
                for (int i = 0, j = 0; i < rgba.Length; i += 4, j += 3)
                {
                    rgba[i] = pixels[j];
                    rgba[i + 1] = pixels[j + 1];
                    rgba[i + 2] = pixels[j + 2];
                    rgba[i + 3] = 255;
                }

                return true;

            case PixelFormat.Gray8:
                for (int i = 0, j = 0; i < rgba.Length; i += 4, j++)
                {
                    rgba[i] = rgba[i + 1] = rgba[i + 2] = pixels[j];
                    rgba[i + 3] = 255;
                }

                return true;

            default:
                rgba = [];
                return false;
        }
    }

    // IImageSource doesn't declare IDisposable (PdfRasterImage never disposes its IImageSource) - the
    // backing Image's Dispose() is itself a near no-op today (see PeachImage.Image.Dispose's
    // remarks), so nothing is actually leaked.
    public void Dispose() { }
}
