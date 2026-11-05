# PeachDrawing.CoreGraphics: a CoreGraphics `Canvas` for macOS and iOS

**What landed.** A new `PeachDrawing.CoreGraphics` package (net10.0-macos/ios, plus net11 under the 11 SDK) with its tests and example, modelled on
[the Skia backend](2026-11-01-skia-backend-and-the-image-codec-seam.md): the consumer supplies a `CGContext`, the canvas borrows it and restores its graphics state, and
`CoreGraphicsTextEngine`/`CoreGraphicsImageCodec` are the optional native text (CoreText) and image (ImageIO, with an `Auto` that keeps encoded bytes only when a Metal
device is present) engines. Default text is `PeachDrawing.Text`, default decoder PeachImage.

**Load-bearing choices.**
- **y-down by flipping once.** `FromContext` concatenates a flip about the bounds (`originAtBottomLeft`, default true); UIKit contexts pass false. Anything CoreGraphics draws
  upright in y-up space (images, tiled images, CoreText lines) is drawn in a locally flipped frame - forgetting that mirrors it. Tests assert top/bottom orientation for images, tiles and text.
- **Layers, tiles and masks are bitmaps, not transparency layers.** `BeginTransparencyLayer` composites with state we could not pin down (blend mode is documented as ignored), so a
  layer is an offscreen `CGBitmapContext` composited back with alpha and blend mode; masks use `DestinationIn`/`DestinationOut` in an isolated bitmap (CoreGraphics' own
  `ClipToMask` has inverted luminance semantics). The offscreen's resolution is the context's current device scale.
- **Effects are Core Image, with sRGB as the working space** (the default linear space makes blurs differ from every other backend). Metal when a device exists, software otherwise.
- **Conic gradients are computed per pixel on the CPU** - CoreGraphics has no angular gradient and a Core Image kernel needs runtime-compiled kernel language that is deprecated.
  A Metal kernel is the obvious follow-up.
- **`CTFontCollection` descriptors report no family through the typed attributes object** (`FamilyName` is null); read `NSFontFamilyAttribute` through `GetAttribute`. The installed-family list was
  empty until that was found by running it. `GetGlyphsForCharacters` reports false for a non-BMP character even when the glyph exists, so glyph presence is `glyphs[0] != 0`. The system's cascade often
  picks a face that is not in the family list, so a fallback face is made from the font's descriptor, not looked up by name.

**Running the tests.** The macOS SDK builds executables as app bundles. xunit.v3's generated entry point blocks the main thread on async work while a macOS main thread has a synchronization
context, which deadlocks discovery; the test project turns that entry point off and runs the runner on a pool thread (`Program.cs`), and sets `UseAppHost` only for xunit's build check. A newer Xcode than
the installed workload fails the executable build; `-p:ValidateXcodeVersion=false` (what `run.sh` passes) is enough for a library of this kind. The tests are not in the diff-coverage gate: they run in
`.github/workflows/coregraphics.yml` (macos-latest) and the project is not in `PeachPDF.slnx`, like `PeachBrackets.Android`.

**Evidence.** 89 tests passing on macOS (bitmap read-back for every Canvas call group, both text engines, all three image codecs with and without a Metal device, layer effects on GPU and CPU, links, lifecycle counters, public
surface, HTML pages painted through the PeachBrackets public API with both text engines); both TFMs build with zero warnings; the example's PNGs were viewed.
**Review pass** fixed: luminosity masks ignored source alpha (Core Image's matrix works unpremultiplied, so the mask is composited over black first), the radial focus not following the ellipse squeeze, `_smooth` not rolling back with a pop, and one failing layer leaking the rest on dispose. **Known and left:** layer bitmaps are resampled when the region is not device-pixel aligned (text in opacity layers can soften), `WithImageInRect` snaps a fractional source rectangle to whole pixels, Core Image intermediates are released by finalizers, and `SuspendClipping` over a non-clip push mirrors Skia.
**Hinting.** `TextHinting` was ignored (the canvas draws unhinted outlines). It now uses `Typeface.TryGetOutline(glyph, OutlineRequest)` as the raster canvas does: the grid-fitted outline is in device pixels with
y up, so its origin is placed through the CTM, snapped vertically, and the points mapped back through the inverse CTM into layout units. Which way is "up" follows the sign of the matrix's `d`. `HintTextOnLowDensityDisplays`
(default off) turns on `Standard` below two device pixels per point. Default stays `None`: Apple does not hint and Retina does not need it.
**iOS simulator.** `src/examples/PeachDrawing.CoreGraphics.iOS.Example` (a UIView drawing through `UIGraphics.GetCurrentContext()` with `originAtBottomLeft: false`, plus pixel self-checks on a bitmap, `run.sh`) ran on an iPhone 17 simulator (iOS 27, Metal available): all checks passed (gradients, conic, text, layer shadow, all three image codecs) and the screenshot is upright. Trap: iOS 26+ aborts at launch (`UIApplicationEvaluateRuntimeIssueForNoSceneLifecycleAdoption`) for an app without a `UIApplicationSceneManifest` and a scene delegate; the sample has both. Not run on a physical device.
**Text vs CoreText (measured; both fixes below were then implemented: `MatchPlatformTextWeight` strokes outline text by 0.0105 em on macOS/Catalyst, merged with synthetic bold into one stroke since bold's 0.02 em would otherwise be hidden; the native engine now draws with exact sub-pixel positions. Result at the defaults: peach/CoreText ink 0.85 -> 0.94-0.99, pixel diff down 15-45%).** Same font file through both engines, 1x and 2x, 13/16/24 px, macOS bitmap context. Layout is identical (same widths, same ink extents). The visible
difference is weight: CoreText puts down 16-22% more ink. Causes found by switching things off: (1) macOS *font smoothing* (`SetShouldSmoothFonts`) - turning it off gives exactly the shared engine's ink (0.85x); it is a monotone coverage
boost (alpha ~ coverage^0.7 at 1x, ^0.5 at 2x), not geometry; a fill + stroke of ~0.0105 em fits it to first order at every size/scale (diff to CoreText 0.155 -> ~0.095). (2) CoreGraphics quantizes glyph origins (jitter of
+-0.25 px per glyph); with `SetShouldSubpixelPositionFonts(true)` + `SetAllowsFontSubpixelQuantization(false)` + `ShouldSubpixelQuantizeFonts(false)` the shared engine matches unsmoothed CoreText to 1.6-4.6% of ink at 16-24 px (21% at 13 px 1x,
unexplained). iOS (simulator) has no smoothing: CoreText/shared ink = 0.95-0.99. Missing from the bindings: `SetAllowsFontSubpixelPositioning` is `SetAllowsSubpixelPositioning`, quantize setter is `ShouldSubpixelQuantizeFonts`.
**Matching the system font and Apple's placement (measured against CoreText, then implemented).** (1) `AutomaticOpticalSize`: San Francisco's `opsz` axis is [17, 28, 96] and defaults to 28 (display cut); CoreText sets it to the size in points (so 17 below 17pt). The default engine now does the same
(`'o'` advance 1210 at opsz 17, 1087 at 28), which made widths exact at 12pt. (2) What was left was a constant per-glyph residual by size (-0.0156 em at 15pt, -0.0254 at 17, -0.0225 at 20, +0.0137 at 28): the font's `trak` table, which
`PeachDrawing.Text` did not read. `TrackingTable` + `Typeface.GetTracking(sizeInPoints)` (public, design units) and `ApplyFontTracking` in the canvas (added to every advance including the last; measure, draw, ink crossings and outlines agree). Widths now match CoreText
within 0.02 px at 9-48pt (0.3px at 34pt, unexplained). (3) `GlyphPlacement.Platform`: CoreGraphics' default rounds the baseline to a whole device pixel and floors each glyph origin to 1/n px, n = clamp(ceil(100/3 / size_in_device_px), 1, max), max 5 on macOS and 3 on iOS
(thresholds 8.4/11.2/16.7/33.4 px = 33.33/4, /3, /2, /1; identical for Helvetica and San Francisco and at 1x and 2x; iOS simulator thresholds measured with a sweep in the iOS example). The default engine emulates it and matches CoreGraphics' own boundaries
at 9/10/14/20/32/40 px (a test, and the iOS example's self-check). Not documented by Apple: the sources found only describe the flags; a Ghostty commit reports thirds. The native engine's exact-positioning settings (`SetShouldSubpixelPositionFonts` etc.) apply only for `Exact`.
**Not verified:** a physical device, the no-Metal path on iOS, and net11.
