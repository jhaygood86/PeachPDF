# CoreText system fonts, Apple's hvgl outlines through libhvf, and the Apple generic families

**Where:** `PeachDrawing.Text` (`Internal/Fonts/Apple/`, `Internal/Fonts/SystemFontStorage.cs`,
`Internal/Fonts/OpenType/Hvgl/`, `GenericFamilyTable`), plus the generics wiring in `PeachBrackets`/`PeachPDF`.

## What changed
- On macOS, Mac Catalyst, iOS and tvOS the only font source is CoreText. The directory scan is gone. A font that CoreText
  backs with a readable `.ttf/.otf/.ttc/.otc` is named by that file; every other font is named by a *virtual path*
  (`coretext:<PostScript name>`, or `coretext:@ui:<design>` for the system interface fonts) and is put back together from
  `CTFontCopyTable` by `SfntAssembler`. `SystemFontStorage` is the one place a path is opened, so the resolver, the
  description reader and the coverage reader did not need to learn about it.
- Fonts with an `hvgl` table and no `glyf`/CFF (PingFang SC/TC/HK/MO on the macOS used here) are drawn by Apple's `libhvf` through
  `[LibraryImport]` and an `[UnmanagedCallersOnly]` callback. `TypefaceExporter` sends them down the static-CFF route.
- `system-ui`, `-apple-system`, `BlinkMacSystemFont` and the new `ui-serif/ui-sans-serif/ui-monospace/ui-rounded`
  generics resolve to San Francisco, New York, SF Mono and SF Rounded; macOS and iOS share WebKit's defaults
  (Times/Helvetica/Courier/Apple Chancery/Papyrus).

## Found by running it, not reading it
- **CoreText's list of available fonts leaves out the system interface fonts** (SF, New York, SF Mono, SF Rounded: the
  files exist in `/System/Library/Fonts` but are not in `CTFontCollectionCreateFromAvailableFonts`). Dropping the directory
  scan lost them silently; they are asked for by design through `CTFontCreateUIFontForLanguage` and the
  `kCTFontUIFontDesign*` traits. Their family names (`System Font`, `.New York`, `.SF NS Mono`, `.SF NS Rounded`) are not
  guessable, so `FontResolver.AppleUiFamilyName` finds the family through the font itself.
- **The hvf render callback gets a NULL parameters pointer** for BeginPath/ClosePath/EndPath. Reading the payload
  unconditionally threw inside the callback, the catch returned `Stop`, and `HVF_render_current_part` answered `-1` with no
  other symptom. The first BeginPart arrives fine, so it looked like the library rejecting the table. A plain C program
  that did the same calls is what showed the table and the calls were right. Read a payload only for AddPoint/AddLine (2
  doubles), AddQuad (4), AddCubic (6).
- HVF draws a straight edge as a quad whose control point is one of its ends; the builder turns those into lines.
- `.Apple Color Emoji UI` (a face of `Apple Color Emoji.ttc`) and the emoji face itself have OS/2 win metrics of zero. That
  made `CellAscent` 0 (a `Debug.Assert` in `TextMeasurement`, and ten `PeachBrackets.Tests` failures on a Mac, on main
  too). A cell with no height is no metric: `hhea` is used then. The fallback picker also now prefers families without a
  leading dot over the ones Apple keeps for its own interface when both cover a character.
- `StaticCffFontBuilder` wrote an `hmtx` without the left side bearings of the glyphs after the last wanted one; a font of
  47 000 glyphs made the file unreadable (the CFF2 route had only escaped because a small font reads garbage instead of
  hitting the end of the file).

## Not done, and why
- iOS itself was not run (no iOS app target in the repo builds with the simulator here). The virtual-path route (assemble
  from tables, read description and cmap from the assembled bytes) is exercised on macOS by `AppleSystemFontsTests`, which
  forces every font through it.
- `FontDefaults.DefaultFamily` is the system font (San Francisco) on macOS, iOS, Mac Catalyst and tvOS, as asked, with
  Arial or the nearest installed family when CoreText has none. 26 engine fixtures were calibrated to Arial's widths and a
  static bold face (San Francisco is variable and has no Hebrew), so `EngineTestDefaults` keeps `DefaultFont` at Arial for the
  engine test assembly on an Apple host; `DefaultFontResolver.DefaultFont` therefore has an internal setter. The iOS-only
  static-initialization order trap (the array of alternatives must be declared before `DefaultFont`) was found by running
  the probe on the simulator.

## Evidence
`PeachDrawing.Text.Tests` 6680, `PeachBrackets.Tests` 663 (was 10 failing on this Mac), `PeachBrackets.Engine.Tests` 18177,
`PeachPDF.Tests` 1615, and the PDF/Svg/Skia/Core/Cli suites all pass on net10.0. A sample page with every generic and
PingFang at three weights, rasterized with PDFium and MuPDF, agrees between them. `dotnet publish -p:PublishAot=true -r
osx-arm64` has no trim or AOT warnings and the binary renders the same fonts.
