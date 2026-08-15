# net10.0 raster image decode/encode ported from StbImageSharp to PeachImage; net8.0 unchanged

Experimental, TFM-gated port: on net10.0, `PdfSharpCore/Utils/PeachImageSource.cs` (new,
`#if NET10_0_OR_GREATER`) implements `ImageSource` via the PeachImage NuGet package (0.1.2) instead of
StbImageSharp/StbImageWriteSharp. `StbImageSource.cs` (the existing implementation) is now itself
`#if !NET10_0_OR_GREATER`-guarded and remains the net8.0 codec unchanged. `PeachPDF.csproj` conditions
both package sets by `$(TargetFramework)` so neither dependency ships in the wrong TFM's package.
`XImage.cs`'s two `ImageSourceImpl ??= ...` default-wiring sites pick the TFM-appropriate type via a new
`CreateDefaultImageSourceImpl()` helper.

## Why PeachImage decode always normalizes to Rgba32 itself

PeachImage's own `DecoderOptions.TargetPixelFormat` converter (`PixelFormatConverter.ConvertIfNeeded`)
only covers Gray8/Rgb24/Rgba32 conversions - it doesn't handle Cmyk32 (Adobe CMYK JPEG) or the
16-bit-per-channel formats (Gray16/Rgb48/Rgba64, producible by PNG) at all, and throws if asked to. Since
`IImageSource.SaveAsJpeg`/`SaveAsPdfBitmap` need a single uniform RGBA8 buffer regardless of the source
format (matching what StbImageSharp always produced by decoding straight to
`ColorComponents.RedGreenBlueAlpha`), a new `PixelFormatNormalizer.ToRgba32` (also net10.0-only) handles
the full `PixelFormat` matrix itself: Gray8/Rgb24/Cmyk32 expand directly, Gray16/Rgb48/Rgba64 downsample
each channel via `BitConverter.ToUInt16(...) >> 8`. Cmyk32→RGB uses the common naive
`255 - min(255, channel + K)` formula (not colorimetrically accurate, but consistent with what a rough
approximation needs here).

## The BMP round-trip had to be checked byte-for-byte, not assumed

`PdfImage.ReadTrueColorMemoryBitmap` (used whenever an image is treated as transparent - i.e. any PNG
source, by the pre-existing magic-byte-sniff heuristic this port kept unchanged) hand-parses the BMP bytes
`IImageSource.SaveAsPdfBitmap` produces, expecting a specific 108-byte BITMAPV4HEADER layout, BI_BITFIELDS
compression, and BGRA pixel byte order when the source has alpha. This is exactly the header shape
`PeachImage.Formats.Bmp.Encoding.BmpImageEncoder.EncodeRgba32`/`WriteV4Header` produces (confirmed by
reading its exact byte offsets against `ReadTrueColorMemoryBitmap`'s parsing, and separately confirmed by
the pre-existing `ImageDrawingTests.PDF_with_Images`/`PDF_with_Image_from_stream` tests - TFM-neutral,
unchanged, and already exercise exactly this path - passing green on net10.0) - so no encoder-shape
workaround was needed. That said, `ReadTrueColorMemoryBitmap`'s own format-validation guard turned out to
be a no-op for this header shape (a pre-existing `?:`/`||` operator-precedence bug, not something this
port introduced or fixed) - see
[pdfimage-readtruecolormemorybitmap-bigheader-guard-is-a-no-op](../invariants/pdfimage-readtruecolormemorybitmap-bigheader-guard-is-a-no-op.md).
Confirmed by actual rasterization (not just "didn't throw") that alpha survives the round trip correctly:
a 40x40 PNG at 50% alpha (RGB 30/200/90) rendered over a white page background via the real
`PdfGenerator`→CLI pipeline, rasterized with both pypdfium2 and MuPDF per this repo's two-renderer
convention, sampled (142,227,172) and (141,227,171) respectively at the image's center - matching the
predicted alpha-blended color (`src*ɑ + bg*(1-ɑ)` ≈ 142/227/172) almost exactly, and neither the opaque
source color (30,200,90, which would mean the alpha channel was dropped) nor plain white (which would mean
the image failed to render). `PeachImageSourceTests.SaveAsPdfBitmap_RoundTripsPartialAlphaExactly` covers
the same alpha-preservation property at the `IImageSource` layer (exact, since it decodes PeachImage's own
BMP bytes losslessly rather than rasterizing through a PDF viewer).

## Decode-failure contract had to be normalized

`ImageLoadHandler.LoadImageFromStream` and `SvgTreeBuilder.DecodeRasterImage` both catch
`InvalidOperationException` specifically to treat a decode failure as non-fatal (leaves the image
unresolved rather than aborting the render) - a contract StbImageSharp's own exceptions apparently already
satisfy. PeachImage throws its own `ImageFormatException` hierarchy instead, which isn't assignable to
`InvalidOperationException` and would have propagated uncaught. `PeachImageSource.Decode` catches both
`PeachImage.ImageFormatException` (a genuine decode failure) and `NotSupportedException`
(`PixelFormatNormalizer.ToRgba32`'s defensive fallback for a `PixelFormat` it doesn't yet handle - dead
code against PeachImage 0.1.0's current format set, but would otherwise crash the whole render instead of
degrading to an unresolved image the moment a future PeachImage version adds one) and rethrows both as
`InvalidOperationException` to match that existing contract, rather than touching the two (TFM-neutral,
shared) call sites. `PixelFormatNormalizerTests`/`PeachImageSourceTests` cover the reachable half of this
(malformed bytes); the `NotSupportedException` half stays defensive-only against PeachImage 0.1.2's
current `PixelFormat` set.

## GIF: briefly a gap, closed within the same change

PeachImage didn't implement GIF as of the NuGet-published 0.1.0, so this port initially shipped with a
real, documented regression: GIF decoded on net8.0 (StbImageSharp) but failed (gracefully, as an
unresolved image) on net10.0. A handful of layout/fragmentation tests that used a hardcoded GIF data URI
purely as an incidental "some decodable image" fixture (nothing GIF-specific about the tests) were
switched to an equivalent PNG data URI (`RasterPngFixture.OnePixelDataUri`, a real encoder-produced
literal, not a hand-picked one) so they'd stay decode-format-agnostic regardless of which TFM's codec was
running. Before this change shipped, PeachImage published 0.1.2 with a GIF codec (`GifDecoder` produces
`PixelFormat.Rgba32`/`Rgb24`, both already handled by `PixelFormatNormalizer` unmodified), so the
`PackageReference` was bumped and the gap closed in place - no `PeachImageSource`/`PixelFormatNormalizer`
code changes were needed, confirming the format-conversion matrix genuinely was already complete for GIF.
Verified, not just trusted: `PeachImageSourceTests.FromBinary_Gif_DecodesCorrectly` and
`SaveAsPdfBitmap_RoundTripsGifTransparencyExactly` (a real transparent 1x1 GIF decodes to the right
dimensions and its alpha survives the BMP round trip), plus an end-to-end rasterization check (a solid
220/40/130 GIF rendered through the real `PdfGenerator`→CLI pipeline came back (220,40,131) via pypdfium2
- within JPEG-lossy tolerance of the source color, confirming GIF goes through the same non-transparent
JPEG-embed path a BMP/JPEG source would, since the pre-existing PNG-magic-byte-sniff `Transparent`
heuristic doesn't specially detect GIF transparency - a pre-existing property of that heuristic, not
something this port changed). TGA and HDR remain net8.0-only; PeachImage has no codec for either.

## Evidence

Full suite green on both TFMs: net8.0 8838 passed, net10.0 8854 passed (net10.0 has more - new
`PeachImageSourceTests`/`PixelFormatNormalizerTests`, including the GIF-specific ones), zero warnings on
`dotnet build PeachPDF.slnx -t:Rebuild`. Diff coverage 98% (`diff-cover` against `origin/main`, net10.0
run, before the 0.1.2 bump) - the only two uncovered lines are a genuinely-unreachable
`default: throw NotSupportedException` defensive branch in the pixel format switch, and a no-op
`Dispose()` that mirrors `StbImageSharpImageSource`'s equivalent (neither `IImageSource` nor any caller
ever invokes it - `XImage` never disposes its `_source`).

A post-change review pass (8 parallel finder angles + verification, per this repo's convention) turned up
and fixed: the `PixelFormatNormalizer.ToRgba32`/decode-failure exception-normalization gap and the missing
alpha/GIF-contract test coverage described above; `IsPng` deduplicated onto the shared `ImageSource` base
class instead of copied per-TFM; `PeachImageSource`'s three `FromXImpl` methods collapsed to share one
decode path; a redundant double buffer-copy removed from `FromStreamImpl` (the common case - the sole
production caller already hands in a `MemoryStream` - now decodes with zero extra copies); the
Gray16/Rgb48/Rgba64 cases in `PixelFormatNormalizer` collapsed into one parametrized loop; a duplicate
test-fixture helper in `PeachImageSourceTests` pointed at the shared `RasterPngFixture` instead; and the
stale StbImageSharp/GIF/TGA/HDR/PSD claims in `docs/architecture.md`, `docs/usage-examples.md`, and
`docs/html-css-support.md` corrected for the TFM split. One suggested simplification was considered and
rejected on inspection: relying on PeachImage's own `TargetPixelFormat` decode-time converter instead of
`PixelFormatNormalizer`'s hand-written matrix looks appealing, but reading its actual per-format source
(`PeachImage.Formats.{Png,Bmp,Jpeg}.Decoding.PixelFormatConverter`) shows it has no direct single-hop
conversion from `Gray16`/`Rgb48` to `Rgba32` and no `Cmyk32` handling at all - and distinguishing "this
conversion just isn't supported" from "the file is genuinely corrupt" by exception type alone isn't safe,
so the custom normalizer stays as the single, uniform, always-correct path.

Performance: a throwaway scratch console app (ProjectReference to `PeachPDF.csproj`, temporarily added to
its `InternalsVisibleTo` and removed again afterward - not committed) timed decode + `AsJpeg()`/`AsBitmap()`
encode of the same 640x480 PNG/JPEG/BMP fixture files, 200 iterations each, `dotnet run -c Release` on
both TFMs. PeachImage (net10.0) was faster overall - roughly 23% less total wall-clock across all 9
decode/encode combinations than StbImageSharp (net8.0) - with PNG→JPEG encoding the one exception at
~11% slower; every other combination was at parity or meaningfully faster (BMP encode ~70-75% faster,
PNG/BMP decode ~25-45% faster). Not a tracked CI benchmark - a one-time sanity check confirming the port
isn't a performance regression, per the porting request.
