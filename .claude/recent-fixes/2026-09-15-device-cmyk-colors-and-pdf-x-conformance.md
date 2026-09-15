# CSS `device-cmyk()` colors, PDF/X conformance, real ICC conversion, and CMYK TIFF (issues #1083/#1084/#1090/#1096)

New `device-cmyk()` CSS Color 5 parsing, a CMYK-native color representation carried all the way from the
cascade to the PDF content stream with no sRGB approximation anywhere, a full `PdfXConformance`
(X1a/X3/X4) surface, (Phase B, once PeachImage 0.4.4 shipped mid-session) real colorimetric ICC
device-to-device conversion via `ColorOptions.ConversionMode`, and CMYK TIFF re-enablement (#1096,
originally deferred as out of scope for Phase A/B, then pulled back in and closed in the same change - see
below). Two-phase plan against the epic [#1090](https://github.com/jhaygood86/PeachPDF/issues/1090) -
Phase A shipped first (buildable with PeachImage 0.4.3 as-is); Phase B was gated on PeachImage growing a
device-to-device ICC API it didn't have yet (`ConvertToSrgb` only went device-&gt;sRGB). A request for that
API was sent to the `peachimage-03` session mid-Phase-A; it shipped PeachImage 0.4.4
(`IccColorProfile.ConvertTo` + black point compensation + TIFF ICC tag 34675 support) within the same
session, so Phase B followed immediately rather than waiting as a separate future change.

## Load-bearing idea

**No naive RGB&lt;-&gt;CMYK approximation is computed anywhere** - a deliberate project-wide decision. A
`device-cmyk()` color is carried through three structs (`PeachPDF.CSS.Color`, `RColor`, `XColor`) as real
CMYK components, and PDF's own support for a document mixing `DeviceRGB`/`DeviceCMYK` content (page
`ColorMode.Undefined`, now `PdfGenerator`'s document-wide default instead of `PdfSharpCore`'s internal
default of `Rgb`) means an RGB-authored color elsewhere on the same page needs no conversion either. The
one narrow exception is achromatic (gray/black) RGB under PDF/X-1a's CMYK-only restriction
(`ColorOptions.BlackGeneration`/`PdfXColorSpaceGuard`) - an exact, lossless ink mapping, not an
approximation, which is why it was in scope while general RGB-&gt;CMYK conversion was not.

## Two real bugs found by running it, not by reading it

Both invalidated an assumption the plan's own research had stated as settled fact, and both were only
caught by actually generating a PDF and inspecting the emitted operators - a passing unit test at the
`CssValueParser` layer gave zero signal either was wrong.

1. **`PdfGraphicsState.RealizeFillColor`/`RealizePen` never actually implement `PdfColorMode.Undefined`'s
   per-color dispatch**, despite `PdfEncoders.ToString` correctly picking CMYK/RGB from a color's own
   `XColor.ColorSpace` under `Undefined`. Both call sites have their own separate `if (colorMode !=
   PdfColorMode.Cmyk) { ...always emit rg/RG... } else { ...always emit k/K... }` branch that hardcodes
   the *literal* `PdfColorMode.Rgb`/`.Cmyk` into `PdfEncoders.ToString`'s second argument rather than
   passing `colorMode` through - so under `Undefined` (`!= Cmyk` is always true) every color silently
   wrote as RGB regardless of its actual tagged space. Caught by generating a real PDF with a
   `device-cmyk(0 1 1 0)` text color and finding `1 0 0 rg` in the content stream instead of a `k`
   operator - the RGB numbers were the exact correct naive-formula conversion of the CMYK input, which is
   what gave away that `EnsureColorMode`'s `Rgb`-forcing branch (reading a CMYK `XColor`'s own naive
   `.R/.G/.B` shadow values) was firing despite `ColorMode` genuinely being `Undefined` at the document
   level (confirmed directly via a throwaway debug test reading `PeachPdfDocument.PdfDocument.Options.ColorMode`
   back out). Fixed with `PdfGraphicsState.ResolveEffectiveColorMode(colorMode, color)`, called before both
   branches, plus a dedup-cache fix (`_realizedFillColor`/`_realizedStrokeColor` must also check
   `.ColorSpace` equality, not just `.Rgb`/numeric equality, or a mixed-space document could skip
   re-emitting the operator when switching spaces between two shapes that happen to share the same derived
   RGB numbers).
2. **`PdfPage.cs`'s `/Group` (transparency-group) gate treated `ColorMode == Undefined` as "the caller
   opted out of this feature entirely, skip the group"** - genuine PDFsharp-fork-inherited behavior, not a
   bug in isolation, but a landmine for this change specifically: since `PdfGenerator` now sets
   `ColorMode = Undefined` as the *default* for every document (not an opt-out signal), the existing gate
   silently stopped adding `/Group` to any page using opacity/a semi-transparent gradient stop/an SVG
   `<mask>` - breaking `PdfA2B_OpacityContent_Succeeds_TransparencyPermitted` (caught by the full
   regression suite, not by anything CMYK-specific). Fixed by dropping the `!= Undefined` condition
   entirely - the `/CS` fallback two lines below already resolves to `/DeviceRGB` for anything but a
   whole-document-forced `PdfColorMode.Cmyk` (never set under Phase A), so the group's own content is
   unaffected.

## Phase B: real ICC conversion (`PdfColorConversionGuard`)

Once PeachImage 0.4.4 published `IccColorProfile.ConvertTo(destination, deviceValues, destinationValues,
pixelCount, intent, blackPointCompensation)` (device-to-device, routed through both profiles' shared D50
PCS), `ColorOptions.ConversionMode` (`ConvertToOutputIntent`/`ConvertToProfile`/`GrayscaleViaK`) became real
colorimetric conversion - still zero naive-formula fallback anywhere. `PdfColorConversionGuard.ApplyConversion`
runs in `PdfGraphicsState.RealizeFillColor`/`RealizePen`, between `EnsureColorMode` and `PdfXColorSpaceGuard`
(so a color is already in its final converted space by the time X1a's CMYK-only check runs). A color's
*source* profile is always well-defined: RGB is always the bundled `PdfAResources.SRgbIccProfile` (CSS
colors are sRGB by definition outside `device-cmyk()`); a `device-cmyk()` color has no source profile
(uncalibrated ink by definition, CSS Color 5 §6) unless `ColorOptions.FallbackCmykProfile` supplies one, so
without that set, `device-cmyk()` colors are left exactly as authored even under a conversion mode - proven
by a real test (`ConvertToOutputIntent_DeviceCmykColor_WithoutFallbackProfile_StaysUnconverted`), not just
asserted in a comment. Parsed `IccColorProfile` instances are cached in a `ConditionalWeakTable<byte[],_>`
keyed by the caller's own profile-byte-array reference, since a solid-color-heavy document realizes the
same few colors/profiles repeatedly.

**"Whole-document forced-CMYK mode" (the plan's other named Phase B deliverable) turned out to already be
subsumed by `ConversionMode`** - setting `PdfDocumentOptions.ColorMode = Cmyk` at the document level would
only reactivate `ColorSpaceHelper.EnsureColorMode`'s *naive*-formula forcing branch, which is exactly what
this whole project deliberately avoids. Since `ConvertToOutputIntent`/`ConvertToProfile` against a CMYK
destination already makes every color real, ICC-converted CMYK before `EnsureColorMode` would matter (it
runs on an already-`Undefined`-mode document, so it's a no-op regardless), a separate document-level toggle
was correctly identified as redundant-and-harmful rather than built.

## CMYK TIFF re-enablement (`InitializeCmykRaster`, closing #1096)

Initially deferred as its own substantial feature rather than a flag flip - TIFF's existing embed path
(`PdfImage.InitializeNonJpeg` -&gt; `ReadTrueColorMemoryBitmap`) is a GDI+-style BMP encoder that assumes
RGB/ARGB throughout, and TIFF has no `/DCTDecode`-equivalent PDF pass-through filter the way JPEG does (a
CMYK JPEG embeds via byte-for-byte pass-through of the original file bytes; a CMYK TIFF has no equivalent
container to pass through, so its decoded pixels have to be re-encoded into the PDF stream). Pulled back
into scope in the same session on explicit instruction and closed as a parallel path alongside the existing
JPEG one, comparable in size to the original #1085 JPEG work:

- New `CmykRasterData` (`Data` decoded CMYK32 pixel bytes, optional `IccProfile`) alongside the existing
  `JpegPassthroughData`, both hanging off `IImageSource`/`XImage`. `XImage.Initialize()` was restructured to
  route three ways instead of two: JPEG-with-passthrough -&gt; `Jpeg` format (existing), CMYK-without
  -passthrough (i.e. TIFF) -&gt; `Tiff` format (new), everything else -&gt; the original Transparent-based
  RGB/Gray logic - the initial design risk here was extending `IsCmyk` to TIFF naively, which would have hit
  `Initialize()`'s pre-existing hardcoded "all CMYK sources are JPEG" assumption; caught by reading that code
  path carefully before writing any TIFF decode logic, not by hitting the bug afterward.
- `PeachImageSource.DecodeCmyk` now dispatches on decoded format: the pre-existing JPEG path is unchanged
  (renamed to `DecodeCmykJpeg`); a new `DecodeCmykRaster` path decodes via ImageSharp's `Cmyk32` pixel
  format, extracts an embedded ICC profile the same way the JPEG path already does
  (`TryGetUsableIccProfileBytes`), and returns the raw decoded bytes with no resize/recompute step.
  `PdfImageTable.ComputeTargetPixelSize` already short-circuited on `image.IsCmyk` before this change (added
  for the CMYK JPEG passthrough case, since a pass-through embed can't be resized) - that same short-circuit
  now also correctly covers CMYK TIFF for a different reason (no resize step exists for the raw-raster path
  either), so no change was needed there beyond a comment.
- `PdfImage.InitializeNonJpeg` now checks `_image.IsCmyk` first and routes to new `InitializeCmykRaster()`,
  which embeds the raw CMYK bytes as `/FlateDecode` with a `/DeviceCMYK` or `/ICCBased` color space (via the
  same `BuildDeviceOrIccColorSpace` helper extracted from the existing JPEG color-space code, rather than
  duplicating the `/ICCBased`-stream-construction idiom a third time) - a genuinely new encode path, not a
  copy of the original file's bytes the way JPEG passthrough is.
- Test fixtures: `CmykTiffFixture` (test assembly) and `ShowcaseCmykTiffFixture` (TestHarness, extended with
  a `BuildCheckerboard` variant for visual interest) both hand-build a minimal baseline TIFF (uncompressed,
  single-strip, chunky, optional ICC tag 34675) from scratch, verified byte-correct via a one-off debug
  decode before being trusted in real tests - PeachImage's TIFF codec is decode-only, so there's no
  round-trip encoder to build a real one from, and a real scanner/press CMYK TIFF is typically megabytes.
- `.claude/accepted-gaps/cmyk-tiff-unsupported.md` is deleted; the `docs/html-css-support.md` `img` row's
  "not currently supported" note was replaced with a description of the raw-`/FlateDecode` embed mechanism,
  and `docs/usage-examples.md` gained a paragraph explaining it alongside the existing CMYK/YCCK JPEG one.

## What was deliberately not done (all recorded as accepted gaps)

- **`device-cmyk()` in `color-mix()` or as a gradient stop** - rejected outright (invalid declaration /
  `NotSupportedException` respectively) rather than mixed/interpolated. See
  `.claude/accepted-gaps/device-cmyk-color-mix-and-gradient-stops-unsupported.md`. The gradient guard
  (`PdfSharpAdapter.RejectCmykGradientStops`) matters structurally, not just as a nicety: a CMYK stop
  reaching `PdfShading` unguarded would corrupt the shading dictionary (document-wide `/ColorSpace` vs a
  4-component `/C0`/`/C1` under a `/DeviceRGB` declaration).
- **PDF/X's PDF-version header** - stays at 1.7 for every `PdfXConformance` level rather than downgrading
  to the ISO-15930-part-specific base version (1.3/1.4/1.6), since getting that right needs verification
  against the real spec text this change didn't have budget for. See
  `.claude/accepted-gaps/pdf-x-version-header-left-at-1-7.md`.
- **PDF/A's own `PdfACmykImageGuard`-style restriction was not extended to fill/stroke colors** - only
  images were ever gated on "CMYK content needs an ICC profile under PDF/A"; a `device-cmyk()` fill color
  under `PdfAConformance` is unrestricted today (PDF/A permits mixed color spaces generally, and no
  evidence was found that an sRGB-only `OutputIntent` alongside `DeviceCMYK` content is itself a
  conformance violation, but this wasn't verified against a real preflight tool either).

## A third bug, in the verification tooling itself: `diff-cover` silently excludes untracked new files

`git diff origin/main...HEAD` (the comparison `diff-cover` uses) shows tracked-file changes but not
brand-new, never-`git add`ed files - so every diff-coverage number this change measured *before staging*
(reported as "100%"/"98%" at various points) was quietly excluding entire new files
(`PdfColorConversionGuard.cs`, `PdfXColorSpaceGuard.cs`, `ColorOptions.cs`, etc.) from both the numerator
and denominator, rather than correctly counting their uncovered lines against the gate. Caught by noticing
a large new file (`PdfColorConversionGuard.cs`, ~140 lines) wasn't listed in `diff-cover`'s per-file output
at all despite genuinely having uncovered lines (confirmed directly against the raw `coverage.cobertura.xml`
via a one-off script) - a 100% report that should have been impossible if the file were actually being
measured. Fixed by `git add`-ing the whole change before running `diff-cover` (staging, not committing);
the true diff-coverage total jumped from 280 measured lines to 380 once every new file was actually
counted. Worth remembering for any future non-trivial change in this repo: **stage new files before trusting
a `diff-cover` number**, or a genuinely under-covered new file can silently pass the 90% gate by not being
measured at all.

## Evidence

- `PeachPDF.Tests` full suite: 11789 passed, 0 failed, 9 skipped (net8.0), after Phase A + Phase B + CMYK
  TIFF combined. One unrelated test (`GraphicsAdapterInkCrossingsTests.LetterSpacing_SpreadsTheCrossingsApart`)
  failed once in one full-suite run under coverage collection and passed cleanly both in isolation and in
  every plain full-suite run with no code changed in between - a pre-existing order-dependent flake, not a
  regression from this change.
- Diff coverage (`diff-cover` against `origin/main`, **with all new files staged** - see above): 98%
  (445/453 lines) including the CMYK TIFF work. The remaining 8 lines are the same pre-existing gaps noted
  above (`PdfColorConversionGuard`'s unreachable `default` arm / unreached Gray-source branch, the
  `AbsoluteColorimetric`-needs-`wtpt` line) plus 4 lines in `PeachImageSource.DecodeCmykRaster` covering an
  ImageSharp decode-failure path that would need a deliberately corrupt TIFF fixture to exercise and wasn't
  judged worth the added fixture complexity for defensive-only lines.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors.
- Three new TestHarness showcases (`cmyk_colors`, `pdf_x1a_conformance`, `cmyk_tiff`) rasterized through both
  PDFium and MuPDF - pixel-identical composition between the two renderers in all three cases, confirming
  real, correctly-positioned `k`/`K` CMYK operators and (for `cmyk_tiff`) a correctly-decoded/re-encoded raw
  CMYK raster checkerboard (not just token presence in the content stream - this repo's own stated pitfall
  for painting-correctness tests). Phase B's `ConversionMode` tests verify real conversion end-to-end through
  actual generated PDF content (e.g. `GrayscaleViaK_RgbColor_EmitsCmykOperatorWithOnlyKNonzero` structurally
  proves C/M/Y are discarded, not just that some conversion function was called), not mocked. A byte-level
  test (`CmykTiff_DecompressedStream_MatchesSourcePixelsExactly`) decompresses the embedded `/FlateDecode`
  stream straight out of the generated PDF bytes and compares it against the source TIFF's raw pixel buffer,
  proving the raster survives the embed with no reordering/corruption beyond what the checkerboard
  showcase's visual comparison alone would catch.
