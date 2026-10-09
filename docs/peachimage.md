# PeachImage

[PeachImage](https://github.com/jhaygood86/PeachImage) is the pure .NET image codec library PeachPDF decodes every raster image
with, and that [PeachDrawing](peachdrawing.md) encodes the bitmaps it saves with. It reads and writes the formats the web uses
(JPEG, PNG, GIF, WebP, AVIF, BMP, plus TIFF and JPEG XL for reading) as managed code only: no native library, no P/Invoke, no platform
imaging API, so the same code runs on Windows, Linux, macOS, Android and in the browser. It targets .NET 8 and .NET 10.

PeachImage is a project of its own, released under the MIT license, and it is not versioned with PeachPDF. This page describes the
version PeachPDF is built with{% if site.data.peachimage %} (**{{ site.data.peachimage.version }}**){% endif %}, and the
[API reference for it](api/PeachImage/index.md) is generated from that exact release, so what you read here and there is what
you get by installing PeachPDF.

```bash
dotnet add package PeachImage
```

You do not need to add it to use PeachPDF: PeachPDF depends on it, so it arrives with the package. Reference it directly when
your own code loads, converts or resizes images, as in the examples below.

## Formats

| Format | Decode | Encode | Notes |
|---|---|---|---|
| JPEG | yes | yes | Baseline and progressive decode and encode; grayscale, YCbCr, RGB, CMYK and YCCK on decode. |
| PNG | yes | yes | Every color type and bit depth, Adam7 interlacing, transparency, the common ancillary chunks, optional palette quantization. |
| GIF | yes | yes | GIF87a and GIF89a, transparency, animation; palette quantization with optional dithering on encode. |
| WebP | yes | yes | Lossy (VP8) and lossless (VP8L), alpha, animation. Lossless is the default on encode. |
| AVIF | yes | yes | Still images; lossy and lossless encode. Animated AVIF is not supported. |
| BMP | yes | yes | Every common header variant and bit depth, RLE, bit-field masks. |
| TIFF | yes | no | Uncompressed, LZW and PackBits; RGB, grayscale, palette and CMYK. |
| JPEG XL | yes | no | Lossy (VarDCT) and lossless (Modular), alpha, animation, 16-bit and floating-point samples, HDR (PQ/HLG) and wide-gamut color, ICC profiles, CMYK. Files made by recompressing a JPEG can be turned back into that exact JPEG. See [JPEG XL](#jpeg-xl). |

The format is detected from the file's contents, so nothing needs registering, and a file with the wrong extension still loads.
A feature a codec does not implement throws a descriptive exception instead of producing a wrong image. Ask the library what it
supports at run time with `Image.SupportedFormats`, which lists each format's name, file extensions, MIME types, and whether it
can decode and encode.

## Loading, converting and saving

[`Image`](api/PeachImage/Image.html) is a decoded raster: its `Width`, `Height` and `PixelFormat`, its pixels, and its
[metadata](api/PeachImage/ImageMetadata.html). Load from a path, a `Stream`, or bytes already in memory (no `MemoryStream` needed),
and save in any format that can encode by naming it:

```csharp
using PeachImage;
using PeachImage.Formats.Jpeg;

using var image = Image.Load("photo.webp");
Console.WriteLine($"{image.Width}x{image.Height} {image.PixelFormat}");

using var output = File.Create("resaved.jpg");
image.Save(output, "jpeg", new JpegEncoderOptions { Quality = 85 });
```

Each format has its own encoder options type under `PeachImage.Formats.<Format>`: `JpegEncoderOptions` (quality, chroma subsampling, progressive),
`PngEncoderOptions` (compression level, color mode, palette size, dithering, interlacing), `GifEncoderOptions`, `WebpEncoderOptions` (lossless or lossy, quality),
`AvifEncoderOptions` and `BmpEncoderOptions`. `SaveAsync` and `LoadAsync` exist for async I/O paths; the encode and decode
themselves are CPU-bound, so only the stream read or write is awaited.

To learn what a file is without decoding it, use `Image.Identify`, which reads only the header:

```csharp
using PeachImage;

using var stream = File.OpenRead("photo.avif");
ImageInfo info = Image.Identify(stream);
Console.WriteLine($"{info.Width}x{info.Height} {info.PixelFormat} ({info.FormatName})");
```

`ImageInfo` also answers a few questions a caller usually wants settled before it decodes:

- `IsLosslessEncoding`: whether the pixel data was encoded losslessly by its own codec, so a caller can avoid silently re-encoding an
  already-lossy source as if it were exact. It is true for WebP's lossless mode, a fully lossless AVIF, every TIFF the decoder supports,
  and a JPEG XL whose colour channels are not XYB-encoded and whose first frame is Modular. It is false for JPEG (always lossy), for
  lossy files, and for a JPEG XL made by recompressing a JPEG (lossless only relative to the original JPEG bytes, not the pixels).
- `Orientation`: the orientation the file records, as an [`ImageOrientation`](api/PeachImage/ImageOrientation.html); see
  [Orientation](#orientation).
- `HasPreview`: whether decoding can produce an early, lower-fidelity version; see [Early previews](#early-previews).
- `IsAnimated`, `HasAlpha`, `IsAdobeInvertedCmyk` and `IsYcck`.

`Image.TryLoad` is the non-throwing form for untrusted input, returning `false` for a stream that is not an image it can read.

## Pixels

The pixel buffer is exposed without copying:

```csharp
using PeachImage;

using var image = Image.Load("photo.png");
Span<byte> pixels = image.GetPixelSpan();
Span<byte> firstRow = image.GetRowSpan(0);
```

`PixelFormat` says how to read it: `Gray8`, `Rgb24`, `Rgba32`, `Cmyk32`, the 16-bit-per-channel `Gray16`, `Rgb48` and `Rgba64`,
and the 32-bit floating-point `GrayF32`, `RgbF32` and `RgbaF32` that high-dynamic-range sources decode to. A decoder keeps the
source's format by default (a CMYK JPEG stays CMYK, so its separations are not thrown away); pass decoder options to have the image
converted to a format you ask for in the same pass.

### Converting pixel formats

`Image.ConvertTo` converts between the gray, RGB and RGBA formats at 8-bit, 16-bit and floating-point depth, for example an HDR
`RgbaF32` decode down to `Rgb24` before encoding it. Integer targets round and clamp, color to gray uses BT.601 luma, and a target
without alpha discards the alpha channel. Values are converted as stored, with no tone mapping. The encoders reject the floating-point
formats, so convert first. CMYK images go through `ConvertToSrgb`, which applies the image's embedded profile, instead.

```csharp
using PeachImage;

using var hdr = Image.Load("photo.jxl");            // may be RgbaF32 for an HDR file
using var jpegReady = hdr.ConvertTo(PixelFormat.Rgb24);
```

## Orientation

Cameras and phones often store a photo sideways and record the turn needed to show it upright. `ImageInfo.Orientation` reports that
value as an [`ImageOrientation`](api/PeachImage/ImageOrientation.html), whose members use the EXIF numbering: `Normal` (1),
`MirrorHorizontal` (2), `Rotate180` (3), `MirrorVertical` (4), `Transpose` (5), `Rotate90` (6), `Transverse` (7) and `Rotate270` (8). It
is read from JPEG and TIFF EXIF, a PNG `eXIf` chunk (one placed after the image data is not seen by `Identify`), a WebP `EXIF` chunk,
AVIF `irot`/`imir` properties and the JPEG XL header.

**No decoder applies it.** The pixels and the width and height are always as stored, so a sideways photo loads sideways. Apply the
orientation yourself with `Image.ApplyOrientation`, which comes in three forms: one that returns a new image (or the same instance for
`Normal`), one that writes into a destination image you supply without allocating, and `ApplyOrientationInPlace`, which works for any
size with `MirrorHorizontal`, `Rotate180` and `MirrorVertical` and for square images with the rest. The result's EXIF orientation is
reset to 1 and its resolution values are swapped for a rotation; orientation stored in XMP is not rewritten.

```csharp
using PeachImage;

ImageInfo info = Image.Identify(File.OpenRead("phone-photo.jpg"));
using var image = Image.Load("phone-photo.jpg");
using var upright = image.ApplyOrientation(info.Orientation);
```

PeachPDF does not use `ApplyOrientation`: it draws the stored pixels through a rotation matrix so that an embedded JPEG stays
byte-for-byte unchanged. See the [`image-orientation`](html-css-support.md) property and
[Image orientation](usage-examples.md#image-orientation).

## Early previews

`DecoderOptions.PreviewAvailable` is an `Action<Image>` that `Load` calls, synchronously on the decoding thread, with a lower-fidelity
version of the image while the full decode is still running. `ImageInfo.HasPreview` says whether a file can produce one:

- **JPEG XL:** the file's preview frame, which may be smaller than the image, delivered before the main frame decodes.
- **PNG:** one full-size preview per Adam7 pass except the last (six for an ordinary interlaced image), with the pixels not yet decoded
  filled in from their decoded neighbors.
- **JPEG:** one preview per progressive scan except the last, starting once DC data for every component has arrived.

The callback owns each image it is given and must dispose it, and an exception thrown from it aborts the decode. Previews honor
`TargetPixelFormat`, and the final image is identical with or without a callback. An interlaced GIF, an AVIF thumbnail and a TIFF
reduced-resolution image do not produce previews.

```csharp
using PeachImage;

var options = new DecoderOptions
{
    PreviewAvailable = preview =>
    {
        using (preview)
        {
            ShowPlaceholder(preview);   // your own code
        }
    },
};

using var image = Image.Load("large-progressive.jpg", options);
```

## JPEG XL

JPEG XL is decode-only. The decoder is written in managed code and covers both coding modes (VarDCT and Modular), alpha, premultiplied
alpha, extra channels (spot colors, CMYK), patches, splines, noise, upsampling, animation with full frame blending, PQ and HLG transfer
functions, wide-gamut color and embedded ICC profiles. Samples above 8 bits decode to the 16-bit formats, and floating-point or deeper
samples to `GrayF32`, `RgbF32` and `RgbaF32`; 32-bit integer samples are not supported (the reference decoder rejects them too), and
encoding is not available. A multi-frame file loads as an `AnimatedImage`; EXIF, XMP and the ICC profile are exposed through
`Image.Metadata`. Like every decoder here, it reports the stored orientation through `ImageInfo.Orientation` rather than rotating the
pixels.

A JPEG XL file made by losslessly recompressing a JPEG (what `cjxl` does with JPEG input) carries enough information to rebuild that
exact JPEG file, including its progressive scans, restart intervals, Huffman tables, ICC profile, EXIF and XMP.
[`JxlJpegReconstruction`](api/PeachImage/JxlJpegReconstruction.html) returns those original bytes without decoding any pixels, which is
useful wherever a JPEG can be embedded as-is, such as in a PDF:

```csharp
using PeachImage.Formats.Jxl;

using var stream = File.OpenRead("photo.jxl");
if (JxlJpegReconstruction.TryReconstructJpeg(stream, out byte[]? jpeg))
{
    File.WriteAllBytes("photo.jpg", jpeg);   // identical to the JPEG that was recompressed
}
```

`HasJpegReconstructionData` tells whether a file qualifies without rebuilding it, and `ReconstructJpeg` throws
`JxlUnsupportedFeatureException` for a file that was not made from a JPEG (those decode through `Image.Load` as usual).

## Animated images

GIF and WebP with more than one frame load as an [`AnimatedImage`](api/PeachImage/AnimatedImage.html) with the same load and save shape:

```csharp
using PeachImage;
using PeachImage.Formats.Gif;

var animation = AnimatedImage.Load("clip.gif");

foreach (AnimatedImageFrame frame in animation.Frames)
{
    Console.WriteLine($"{frame.Duration.TotalMilliseconds}ms, disposal={frame.Disposal}");
}

using var output = File.Create("resaved.gif");
animation.Save(output, "gif", new GifEncoderOptions { MaxColors = 128, Dither = true });
```

Frames come out fully composited, with their duration and disposal method. `Image.IsAnimated` and `ImageInfo.IsAnimated` tell you
before you choose which loader to use.

## Resizing

`Resize` offers fifteen resampling filters through [`ResamplingFilter`](api/PeachImage/ResamplingFilter.html), `Bicubic` by default,
including `Lanczos3`, `CatmullRom`, `MitchellNetravali`, `Box` and `NearestNeighbor`. `ResizeMode.Max` treats the size as a bounding
box: it scales down to fit, keeps the aspect ratio and never upscales.

```csharp
using PeachImage;

using var image = Image.Load("photo.jpg");

using var thumbnail = image.Resize(200, 150);
using var sharper = image.Resize(200, 150, new ResizeOptions { Filter = ResamplingFilter.Lanczos3 });
using var fitted = image.Resize(200, 200, new ResizeOptions { Mode = ResizeMode.Max });
```

When the source already fits a `ResizeMode.Max` box, the same instance comes back rather than a copy, so do not keep using the
original after disposing the result in that case. `AnimatedImage.Resize` resizes every frame as the frames are enumerated, keeping
each one's duration and disposal.

## Color profiles and metadata

`Image.Metadata` carries what the file embedded: an ICC profile (`GetIccColorProfile()`), and the raw EXIF, XMP and other profile
blocks as [`RawMetadataProfile`](api/PeachImage/RawMetadataProfile.html) entries. PeachPDF reads the ICC profile to embed an image
with its color space intact, and `ImageInfo.IsAdobeInvertedCmyk` and `IsYcck` tell it when a JPEG's CMYK samples are stored
inverted. See [Color and ICC profiles](usage-examples.md#color-and-icc-profiles) for what PeachPDF does with them.

## Disposal

`Image` implements `IDisposable`, and most instances rent their pixel buffer from a shared pool that `Dispose` returns it to. Not
disposing one is safe: it is collected like any other object, with no leak. Disposing matters for throughput under concurrent
load, such as a service resizing many uploads at once, where reusing buffers cuts allocation and garbage collection. An
`AnimatedImage` and the frames of its `Frames` need no disposal.

## PeachImage in PeachPDF and PeachDrawing

- **PeachPDF** decodes the raster images a document references (`<img>`, CSS images, `data:` URIs) with PeachImage. A CMYK or
  ICC-profiled JPEG is embedded byte for byte rather than converted, and everything else is decoded to RGBA for the PDF writer. See
  [Image loading and decoding](architecture.md#image-loading-and-decoding). A JPEG XL file made by recompressing a JPEG is
  turned back into the original JPEG (without decoding any pixels) and then embedded by the same rules as any JPEG, so a CMYK
  or ICC-tagged one is embedded byte for byte. `ImageInfo.IsLosslessEncoding` is what lets a lossless WebP, AVIF, TIFF or JPEG XL embed
  without a lossy JPEG re-encode, and `ImageInfo.Orientation` is what the [`image-orientation`](html-css-support.md) property reads.
- **[PeachDrawing](peachdrawing.md)** draws into a `RasterSurface` and saves it through PeachImage's encoders
  ([`RasterCanvas.Save`](api/PeachDrawing/RasterCanvas.html) takes a format name and an
  `EncoderOptions`), so any format above that can encode is a format a `RasterCanvas` can save.

## API reference

The [PeachImage API reference](api/PeachImage/index.md) documents every public type and member. It sits beside the references for
[PeachPDF](api/index.md), [PeachDrawing.Core](api/PeachDrawing.Core/index.md), [PeachDrawing](api/PeachDrawing/index.md) and
[PeachDrawing.Text](api/PeachDrawing.Text/index.md), and the pages of one link to the pages of another where they meet: a
`RasterCanvas.Save` page links to the `EncoderOptions` it takes here.

## License

PeachImage is MIT-licensed. Its license, and the notice for the one algorithm it references from libjpeg-turbo, are in its
[repository](https://github.com/jhaygood86/PeachImage); see the [license page](license.md) for the components PeachPDF itself
carries.
