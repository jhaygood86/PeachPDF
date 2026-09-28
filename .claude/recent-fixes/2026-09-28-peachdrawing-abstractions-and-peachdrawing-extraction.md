# PeachPDF's internal rendering-abstraction layer split into two standalone packages

`PeachDrawing.Abstractions` (a `Canvas`/`RenderContext` drawing-surface abstraction) and `PeachDrawing` (a
standalone CPU software rasterizer implementing it, `RasterCanvas`/`RasterRenderContext`/`RasterSurface`)
were extracted out of PeachPDF's internal `Html/Adapters`/`Raster` layers into their own NuGet packages,
versioned in lockstep with PeachPDF via `src/Version.props`. See `docs/peachdrawing-abstractions.md` and
`docs/peachdrawing.md`. Full design rationale lives in the plan file this was executed from
(`as-part-of-a-compiled-hummingbird.md`, not checked into the repo); this entry is the load-bearing summary.

## The load-bearing idea

The end state isn't "extract PeachPDF's internals for reuse" - it's that `PeachDrawing.Abstractions` +
`PeachDrawing` must be usable by someone who has never heard of PeachPDF, and PeachPDF itself becomes one
`Canvas` implementation among potentially several, not the thing the abstraction was designed around. That
reframing forced a real redesign, not a mechanical move: the old adapter types (`RBrush`, `RPen`,
`RGraphicsPath`, `RImage`, `RFont`) were opaque, same-assembly-only handles that raster's own paint code
downcast through to reach real data (`((FontAdapter)font).Font.Typeface`, etc.) - workable only because
exactly one concrete backend family existed. Making every one of them plain, publicly-readable data (a
closed `Brush` discriminated union, a concrete `Pen`, a `GraphicsPath` that records its own segment list,
`Image.GetPixels()`, `Font.Typeface`/`SyntheticStyle`) is what let a second, real backend - `PeachDrawing`'s
own `RasterCanvas` - read another implementation's paint objects with zero downcasting, proving the
abstraction actually works for someone besides its original author.

## What was found only by running it, not by reading it

- **A rename can overshoot its target even when the source string is fully qualified.** Disambiguating
  `RColor`/`RBlendMode`/`RFontStyle`/`RPoint` from unrelated same-named `PeachPDF.CSS` types (a real,
  previously-hit collision - see the naming-collision crisis this session's plan file documents in full)
  led to `RPoint`→`PaintPoint`; a later pass reverting an *unrelated* accidental widening of that same
  rename (`.PaintPoint` used as a member-access suffix, not a type name) needed `.PaintPoint.X`/`.PaintPoint.Y`
  as the discriminator between the real struct and three unrelated members that happened to also read
  "Point" before the rename touched them: `XGraphicsUnit.Point` (PDFsharp's own point-unit enum),
  `XUnit.Point` (a unit-conversion property), and `LightKind.Point` (an SVG point-light-source enum member).
  A python regex doing that revert with a negative lookahead (`\.PaintPoint(?!\.[XY]\b)\b`) still produced
  two false positives of its own on the first pass - a constructor call (`new
  PeachDrawing.Abstractions.PaintPoint(0, 0)` became `...Point(0, 0)`, a type that doesn't exist) and a
  bare cref parameter-list mention - both caught only by reading every changed line's actual diff, not by
  trusting the regex's own logic. **Lesson generalized**: whenever a rename's source or target string can
  appear as a *suffix* in an unrelated name, or inside a constructor call, or inside a cref parameter list,
  read every line of the resulting diff before trusting a scripted revert - the same discipline the
  original rename-crisis postmortem already established, re-earned here on a smaller scale.
- **A doc-comment `<see cref>` pointing at a type outside the referencing assembly's own dependency graph
  doesn't error at compile time unless `GenerateDocumentationFile` is on** - both new packages shipped with
  it off, so 12 dangling crefs (into `PeachPDF.Adapters.*`, `PdfSharpCore.Pdf.Advanced.*`, `Html.Core.Dom.*`)
  and ~225 undocumented public members went unnoticed through every earlier "zero warnings" build in this
  extraction. A post-change review agent pass caught this as a real regression relative to how
  `PeachPDF`/`PeachDrawing.Text` already ship (both already set `GenerateDocumentationFile` +
  `WarningsAsErrors;CS1591`). Fixed by turning both on and either writing real XML docs or (for every
  member that is a plain override of an already-documented `Canvas`/`RenderContext`/`Font` base member)
  using `<inheritdoc/>`, which silences CS1591 without restating the base contract.
- **The AOT smoke test's prior "reached native codegen, then failed on a missing MSVC toolchain" result
  was a sandbox PATH gap, not a real toolchain gap** - `vswhere.exe` and the matching `link.exe` both exist
  on this Windows machine, just not on the Bash tool's PATH. Prepending `vswhere.exe`'s own directory
  (`C:\Program Files (x86)\Microsoft Visual Studio\Installer`) let the full pipeline
  (`PeachDrawing.Text` → `PeachDrawing.Abstractions` → `PeachDrawing` → `PeachPDF` → `PeachPDF.Cli`)
  actually link to a native binary, which was then run end-to-end (`--help`, then a real HTML→PDF render)
  rather than just compiled - stronger evidence than any prior checkpoint in this extraction had.

## What was deliberately not done

- `RasterSurface.Buffer`/`.Bounds` stay `internal` (a pooled, possibly-oversized array and a device-pixel
  integer rect - genuine engine plumbing, not part of "the drawing API"), reviewed once and confirmed
  correct rather than widened for convenience.
- `PeachDrawing`'s `Filters/FilterOps.cs`'s two SVG-descriptor-coupled methods (`BuildTransferLut`,
  `ConvolveMatrix`) and all of `Lighting.cs` stayed PeachPDF-side (`PeachPDF/Svg/SvgFilterOps.cs`) rather
  than inventing new portable parameter types for two methods under time pressure - a proportionate,
  documented boundary, not an oversight.
- No SVG rendering engine was added to `PeachDrawing` - an OpenType `SVG ` table glyph falls back to its
  plain outline on a standalone `RasterCanvas`, exactly as a font with no such table would; PeachPDF's own
  `RenderContext.CreateSvgGlyphPainter` hook supplies the real SVG engine only when PeachPDF is driving it.
  This is a permanent capability gap, not a to-do.

## Evidence

Zero-warning `dotnet build PeachPDF.slnx -t:Rebuild` across every project on every TFM (net8/net10/net11);
full test suite green and unchanged in count throughout (14,414 `PeachPDF.Tests` + 71
`PeachDrawing.Abstractions.Tests` + 115 `PeachDrawing.Tests` + 183 `PeachPDF.Cli.Tests` + 122
`PeachPDF.SourceGenerators.Tests`, 6,527 `PeachDrawing.Text.Tests`); 92% diff coverage against `main`
(`git add -A` first, per [[diff-cover-needs-staged-new-files]]); two-renderer (PDFium + MuPDF)
rasterize-and-look verification at every step that touched paint code, per
[[verify-painting-by-rasterizing-not-self-consistent-tests]]; a real, working standalone quick-start
(compiled and run as a throwaway scratch console app, producing an actual PNG, visually confirmed) proving
the "usable with zero PeachPDF reference" design goal is genuinely met, not just asserted in docs.
