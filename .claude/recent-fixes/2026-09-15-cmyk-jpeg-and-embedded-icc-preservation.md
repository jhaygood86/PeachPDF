# CMYK/YCCK JPEG and embedded-ICC-profile preservation (issue #1085)

Every raster image used to be forced through `Image.Load(stream, new DecoderOptions { TargetPixelFormat
= PixelFormat.Rgba32 })` in `PeachImageSource.cs` before reaching the PDF. For a CMYK/YCCK JPEG this
naively converted CMYK→RGB with no color management (destroying print separations) and then re-encoded
the result as a lossy RGB JPEG. Separately, any embedded ICC profile a JPEG carried (RGB, Gray, or CMYK)
was discarded outright, regardless of color model.

**The fix**: a JPEG source is now embedded via byte-for-byte pass-through (`/DCTDecode`, the original
file bytes unchanged) whenever that's the only way to guarantee full color fidelity - always for a
CMYK/YCCK source (`/DeviceCMYK` or `/ICCBased` when it carries a usable embedded ICC profile, plus a
`/Decode [1 0 1 0 1 0 1 0]` array undoing Adobe's inverted-CMYK convention where needed), and for an
RGB/Gray source only when it carries a usable embedded ICC profile (`/ICCBased` instead of the usual bare
`/DeviceRGB`/`/DeviceGray`) - preserving that profile is the only reason to prefer pass-through over the
existing lossy re-encode for RGB/Gray. `PdfImage.EmbedJpegPassthrough` is the single method
`InitializeJpeg`'s fast path always funnels a CMYK source through (unconditionally - it's never resized,
see below) and an RGB/Gray source through only when it has a usable ICC profile. `PdfACmykImageGuard`
requires a CMYK image to carry an ICC profile under any `PdfAConformance` level - a bare `/DeviceCMYK`
image has no relationship to PeachPDF's RGB-based PDF/A output intent, while a bare RGB/Gray image stays
conformant either way.

There is no separate `XImageFormat`/`InitializeCmyk` for the CMYK case, even though an earlier version of
this change had one - a post-change review pass traced through and confirmed `InitializeJpeg`'s
`JpegPassthrough`-driven fast path *already* handles a CMYK source correctly on its own (CMYK is never
`Transparent`, so it already routes to `XImageFormat.Jpeg`; `PdfImageTable` already forces its
`_targetWidth`/`_targetHeight` to `null`), so the parallel format/method added no behavior and was
removed as pure redundancy.

**Found by running it, not by reading it**: the first implementation checked for an embedded ICC profile
on the image *after* decoding via `Rgba32DecoderOptions` (matching every other format's existing path).
This silently never worked for any real RGB/Gray JPEG - PeachImage's `PixelFormatConverter.ConvertIfNeeded`
returns a **new** `Image` instance whenever the requested `TargetPixelFormat` differs from the source's
native one, and that new instance's `Metadata.Profiles` does not carry over the original decode's. A
JPEG's native RGB/Gray pixel format is always `Rgb24`/`Gray8` (JPEG has no alpha channel at all, so it's
never natively `Rgba32`), so this conversion - and the metadata loss - happened on *every* RGB/Gray JPEG,
unconditionally. Caught only by actually splicing a synthetic ICC profile into a real encoded JPEG and
checking `Image.Metadata.GetIccColorProfile()` came back non-null - the bug was invisible from reading
the code, since `Image.Load` reads as if it should just work. Fixed by decoding an RGB/Gray JPEG
natively (no `TargetPixelFormat` at all - `PeachImageSource.DecodeRgbOrGrayJpeg`), which incidentally
also means no more wasted per-pixel Rgb24→Rgba32 conversion for the common (no-ICC) case either, since
PeachImage's JPEG encoder and resizer already accept Gray8/Rgb24 directly. A CMYK/YCCK source's own
decode (`TargetPixelFormat = Cmyk32`) was never affected by this - a CMYK JPEG's native format already
*is* `Cmyk32`, so no conversion (and no metadata loss) ever happens there.

**A second bug the same fix introduced, caught by a post-change review pass (not by the tests written
alongside the feature)**: deciding to decode an RGB/Gray JPEG *natively* instead of forcing `Rgba32` has
a consequence beyond ICC preservation - a genuinely grayscale source now decodes to `Gray8`, and
PeachImage's JPEG encoder branches on the *source's own* `PixelFormat` (not on anything about alpha), so
re-encoding a `Gray8` buffer now produces a real 1-component grayscale JPEG instead of always emitting
3-component YCbCr the way a forced-`Rgba32` buffer always did. `PdfImage.InitializeJpeg`'s lossy
re-encode fallback (taken whenever a resize is requested - the default, since `DownscaleImages` defaults
to `true`) still hardcoded `/DeviceRGB` unconditionally, so a resized grayscale JPEG would emit a
`/ColorSpace`/stream component-count mismatch: a `/DeviceRGB` (3-component) declaration over a
1-component `/DCTDecode` stream, which is corrupt per the PDF spec. Fixed by adding
`IImageSource.IsGrayscale` (true only when the underlying decode is natively `Gray8`) and branching
`/DeviceGray` vs `/DeviceRGB` on it in the fallback. Confirmed by temporarily reverting the fix and
watching the regression tests (`GrayJpeg_Resized_UsesDeviceGrayNotDeviceRgb`,
`GrayJpeg_NaturalSize_UsesDeviceGray`) fail exactly as predicted, then rasterizing the fixed output with
both PDFium and MuPDF to confirm a resized grayscale gradient renders correctly, not corrupted.

**Deliberately out of scope, by explicit correction during planning**, not an oversight:
- TIFF (the only other PeachImage codec that decodes to `Cmyk32`) is rejected outright for a CMYK source
  (`InvalidOperationException`, same non-fatal contract as TGA/PSD/HDR) rather than given a lesser,
  ICC-less embed - PeachImage's TIFF decoder doesn't yet surface an embedded ICC profile. See
  `.claude/accepted-gaps/cmyk-tiff-unsupported.md` (tracking issue #1096).
- A CMYK image is never resized/downscaled, full stop (`PdfImageTable.ComputeTargetPixelSize` returns
  `(null, null)` unconditionally for one) - PeachImage has no CMYK JPEG encoder to re-encode a resized
  copy with. An RGB/Gray image with an embedded ICC profile *is* still resize-capable; a resize simply
  forfeits pass-through (and the profile) for that one embed, falling back to the ordinary re-encode.
- PNG/WebP/AVIF embedded ICC profiles are not preserved - none of those formats has an equivalent
  byte-for-byte pass-through filter in PDF the way JPEG's `/DCTDecode` does, and forcing them through the
  existing decode-and-Flate-raw-RGB path can't guarantee full fidelity for ICC purposes without real
  additional work (splitting an alpha-bearing image into a color plane + `/SMask` while also carrying a
  3-channel ICC profile).

**Evidence**: `PeachPDF.Tests` unit/integration suite (`PeachImageSourceTests`, new
`CmykImageIntegrationTests`) - 10222 tests passing, diff coverage 97% against `main`. Verified by
rasterizing two separate end-to-end HTML→PDF renders with both PDFium and MuPDF: a real Adobe-authored
CMYK JPEG (confirming the `/Decode` array produces correct, non-inverted colors) and a resized grayscale
JPEG (confirming the `/DeviceGray` fix above renders a correct gradient, not corrupted) - pixel-identical
between both renderers in each case. Full-solution `dotnet build -t:Rebuild` is warning-free. A new
"CMYK JPEG Images" showcase (`src/PeachPDF.TestHarness/Program.cs`) demonstrates the feature. An 8-angle
post-change review pass (correctness, removed-behavior, cross-file, reuse, simplification, efficiency,
altitude, CLAUDE.md conventions) caught the grayscale bug above plus several smaller fixes folded into
this same change: an unnecessary full-buffer copy on every image embed, this file (the migration-notes
entry was initially missing), a test that checked `/Decode`'s presence but not its actual array value,
and a redundant `TryGetUsableRgbOrGrayIccProfile` implementation.
