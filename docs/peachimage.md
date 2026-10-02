# PeachImage

[PeachImage](https://github.com/jhaygood86/PeachImage) is the pure .NET image codec library PeachPDF decodes every raster image
with, and that [PeachDrawing](peachdrawing.md) encodes the bitmaps it saves with. It reads and writes the formats the web uses
(JPEG, PNG, GIF, WebP, AVIF, BMP, and TIFF for reading) as managed code only: no native library, no P/Invoke, no platform
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

`Image.TryLoad` is the non-throwing form for untrusted input, returning `false` for a stream that is not an image it can read.

## Pixels

The pixel buffer is exposed without copying:

```csharp
using PeachImage;

using var image = Image.Load("photo.png");
Span<byte> pixels = image.GetPixelSpan();
Span<byte> firstRow = image.GetRowSpan(0);
```

`PixelFormat` says how to read it: `Gray8`, `Rgb24`, `Rgba32`, `Cmyk32`, and the 16-bit-per-channel `Gray16`, `Rgb48` and `Rgba64`.
A decoder keeps the source's format by default (a CMYK JPEG stays CMYK, so its separations are not thrown away); pass decoder
options to have the image converted to a format you ask for in the same pass.

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
  [Image loading and decoding](architecture.md#image-loading-and-decoding).
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
