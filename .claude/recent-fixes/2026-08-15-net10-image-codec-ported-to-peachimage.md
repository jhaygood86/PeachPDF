# Raster image decode/encode ported from StbImageSharp to PeachImage, on both target frameworks

`PdfSharpCore/Utils/PeachImageSource.cs` (new) implements `ImageSource` via the PeachImage NuGet
package instead of StbImageSharp/StbImageWriteSharp, which it fully replaces - `StbImageSource.cs` (the
former implementation) is deleted. This landed in three stages within the same body of work: PeachImage
0.1.x only targeted net10.0, so the port started out `#if NET10_0_OR_GREATER`-gated with StbImageSharp
kept as the net8.0 codec; once PeachImage 0.2.0 added a net8.0 target alongside net10.0, the TFM guards
were removed and StbImageSharp/StbImageWriteSharp were dropped from `PeachPDF.csproj` entirely; then
PeachImage 0.2.1 closed the pixel-format-conversion gap described below, letting `PixelFormatNormalizer.cs`
be deleted outright (see "PeachImage 0.2.1 closed this gap" further down). `XImage.cs`'s two
`ImageSourceImpl ??= ...` default-wiring sites go through a `CreateDefaultImageSourceImpl()` helper that
now just returns `new PeachImageSource()` unconditionally.

## Why PeachImage decode used to normalize to Rgba32 itself (superseded - see below)

PeachImage's own `DecoderOptions.TargetPixelFormat` converter (`PixelFormatConverter.ConvertIfNeeded`)
only covered Gray8/Rgb24/Rgba32 conversions as of 0.1.x/0.2.0 - it didn't handle Cmyk32 (Adobe CMYK
JPEG) or the 16-bit-per-channel formats (Gray16/Rgb48/Rgba64, producible by PNG) at all, and threw if
asked to. Since `IImageSource.SaveAsJpeg`/`SaveAsPdfBitmap` need a single uniform RGBA8 buffer regardless
of the source format (matching what StbImageSharp always produced by decoding straight to
`ColorComponents.RedGreenBlueAlpha`), `PixelFormatNormalizer.ToRgba32` handled the full `PixelFormat`
matrix itself: Gray8/Rgb24/Cmyk32 expanded directly, Gray16/Rgb48/Rgba64 downsampled each channel via
`BitConverter.ToUInt16(...) >> 8`. Cmyk32→RGB used the common naive `255 - min(255, channel + K)`
formula (not colorimetrically accurate, but consistent with what a rough approximation needs). This
class and its dedicated test file no longer exist - see the next section.

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
something this port changed).

## TGA/PSD/HDR: a real, permanent gap once net8.0 switched too

Unlike GIF, TGA/PSD/HDR were never on PeachImage's roadmap. While the port was still net10.0-only, this
was invisible on net8.0 (StbImageSharp still handled them there). Once PeachImage 0.2.0 made a net8.0
target possible and StbImageSharp was dropped entirely, TGA/PSD/HDR decoding disappeared from every
PeachPDF build - and unlike the GIF gap, this one shipped in a released version (v0.9.12) with working
StbImageSharp-based TGA/HDR/PSD support, so it's a genuine migration, not just an in-flight regression
caught before release. See
[tga-psd-hdr-unsupported](../accepted-gaps/tga-psd-hdr-unsupported.md) and the matching
`.claude/migration-notes/` entry.

## Evidence

Full suite green on both TFMs (net8.0 now shares the same `PeachImageSourceTests`/
`PixelFormatNormalizerTests` net10.0 used to run alone, including the GIF-specific ones - all pass
unchanged since PeachImage's public API is identical across its two TFM builds), zero warnings on
`dotnet build PeachPDF.slnx -t:Rebuild`. Diff coverage 98% (`diff-cover` against `origin/main`, measured
during the net10.0-only stage of this change) - the only two uncovered lines are a genuinely-unreachable
`default: throw NotSupportedException` defensive branch in the pixel format switch, and a no-op
`Dispose()` (neither `IImageSource` nor any caller ever invokes it - `XImage` never disposes its
`_source`).

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
rejected on inspection at the time: relying on PeachImage's own `TargetPixelFormat` decode-time converter
instead of `PixelFormatNormalizer`'s hand-written matrix looked appealing, but reading its actual
per-format source (`PeachImage.Formats.{Png,Bmp,Jpeg}.Decoding.PixelFormatConverter`) showed it had no
direct single-hop conversion from `Gray16`/`Rgb48` to `Rgba32` and no `Cmyk32` handling at all - and
distinguishing "this conversion just isn't supported" from "the file is genuinely corrupt" by exception
type alone wasn't safe. This was fixed upstream, not worked around here - see the next section.

## PeachImage 0.2.1 closed this gap; `PixelFormatNormalizer.cs` is deleted

Asked PeachImage's maintainer directly for exactly the guarantee the rejected-simplification note above
was missing (see the prompt in this repo's session history around 2026-08-16/17); PeachImage 0.2.1
shipped it in commit `83cbfa4` ("Guarantee Rgba32 conversion for every native pixel format"), with its
own `Rgba32ConversionCompletenessTests.cs` explicitly naming PeachPDF as the motivating consumer and
pinning the exact contract needed: requesting `TargetPixelFormat = PixelFormat.Rgba32` now succeeds in
one hop for every native `PixelFormat` any of its six decoders can produce (verified by reading that
test file directly, and independently re-verified with a throwaway scratch program before touching
`PeachPDF`'s code - Gray16/Rgb48/Rgba64 PNGs and a CMYK-shaped case all convert correctly; the CMYK
formula matches the naive one `PixelFormatNormalizer` used to use). The same commit also fixed a latent
bug in the old converter that would have thrown `IndexOutOfRangeException` for every *opaque* `Rgb24`
PNG requesting `Rgba32` - never hit here since PeachPDF never requested a conversion before this change.

`PeachImageSource.Decode` now just calls `Image.Load(stream, Rgba32DecoderOptions)` directly;
`PixelFormatNormalizer.cs` and `PixelFormatNormalizerTests.cs` are deleted, and the `NotSupportedException`
half of `Decode`'s catch clause (previously needed for that class's own defensive default-case throw) is
gone too - it's back to just `catch (ImageFormatException ex)`. `PeachImageSourceTests.
FromBinary_Png16Bit_RoundTripsExactly` is this repo's own spot-check that the guarantee holds through
PeachPDF's real pipeline, not a full re-test of PeachImage's conversion matrix (that lives upstream now).

One API wrinkle surfaced along the way, also fixed upstream rather than worked around here: `Image.Load
(stream)` auto-detects the format from content, so a generic caller can't know which concrete
`XyzDecoderOptions` subtype to construct ahead of time, yet `DecoderOptions` (as of 0.2.1) was abstract -
forcing an arbitrary, format-mismatched-looking subtype pick (e.g. constructing `PngDecoderOptions` for a
call that might decode a JPEG) purely to set `TargetPixelFormat`. Verified this actually worked correctly
regardless of the mismatch (each decoder reads `options?.TargetPixelFormat` via simple property access on
the base-typed parameter, never a downcast) before flagging it upstream as a real ergonomics/discoverability
problem rather than a correctness one. PeachImage 0.2.2 (commit `b6e76d7`) made `DecoderOptions` itself
directly constructible and documented the polymorphic-read guarantee directly on `TargetPixelFormat`, so
`PeachImageSource.cs` now constructs a plain `new DecoderOptions { TargetPixelFormat = PixelFormat.Rgba32 }`
- no per-format subtype, no `PeachImage.Formats.Png` import, no comment justifying why an unrelated
subtype is safe to use here.

## Performance

A throwaway scratch console app (ProjectReference to `PeachPDF.csproj`, temporarily added to its
`InternalsVisibleTo` and removed again afterward - not committed) timed decode + `AsJpeg()`/`AsBitmap()`
encode of the same 640x480 PNG/JPEG/BMP fixture files, 200 iterations each, `dotnet run -c Release`. Not
a tracked CI benchmark - a one-time sanity check confirming the port isn't a performance regression, per
the porting request.

**Stage 1 (net10.0-only, PeachImage 0.1.x vs. StbImageSharp on net8.0 - different runtimes, not a clean
A/B):** PeachImage was faster overall - roughly 23% less total wall-clock across all 9 decode/encode
combinations - with PNG→JPEG encoding the one exception at ~11% slower; every other combination was at
parity or meaningfully faster (BMP encode ~70-75% faster, PNG/BMP decode ~25-45% faster).

**Stage 2 (PeachImage 0.2.0 on both net8.0 and net10.0 - same code, clean A/B against the runtime):**
re-running the identical fixtures/harness against net8.0 (now also PeachImage) surfaced a real, clean,
reproduced-twice finding that stage 1 couldn't have seen at all - **PNG decode is roughly 15x slower on
the net8.0 runtime than on net10.0** (~12ms vs. ~0.8ms per 640x480 PNG; every other operation - JPEG/BMP
decode, all three encode paths - stays within a few percent between the two runtimes, matching PeachImage
0.1.x's net10.0 numbers). PeachImage's PNG decoder has no `#if NET10_0_OR_GREATER`/TFM-conditional code
at all (checked directly), so this isn't a code-path difference; PeachImage's PNG codec uses
`System.IO.Compression` (`ZLibStream`/`DeflateStream`) for the DEFLATE stream, so the most likely
explanation is a real difference in .NET 8 vs .NET 10's underlying native compression library
performance for this workload - a runtime characteristic, not something fixable in `PeachPDF` or
`PeachImage` source. In absolute terms 12ms/image is still fast for a handful of embedded images, but a
document embedding many PNGs (dozens+) on the net8.0 build would see this add up in a way it didn't
before (StbImageSharp's net8.0 PNG decode was ~1.6ms/image, i.e. PeachImage's net8.0 PNG decode is
also ~7x slower than the *previous* net8.0 codec, not just slower than PeachImage's own net10.0 build)
- with every other operation (JPEG/BMP decode, all encode paths) still measurably faster than the old
StbImageSharp net8.0 baseline, matching net10.0's improvement. Worth knowing if PNG-heavy-document
performance on the net8.0 build ever gets reported as regressed; not something this change attempts to
work around.
