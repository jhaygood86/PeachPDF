# Fix PDF gradient shading functions ignoring a first/last stop that isn't at 0%/100%

Reported as visual artifacts on the Charts.css showcase's "Columns" chart: gray gradient washes
below where a horizontal gridline should be, and a spurious vertical line between columns - neither
visible when the same HTML is opened in a browser.

## The bug

Charts.css draws its secondary-axis gridlines with no extra DOM elements: each column's own
`tbody tr` gets `background-image: linear-gradient(color <w>, transparent <w>)` (both stops at the
*same* small offset, e.g. 1px) tiled via `background-size: 100% calc(100% / N)`. A spec-correct
renderer paints a crisp hairline at the top of each tile.

`src/PeachPDF/Html/Core/Handlers/CssImagePainter.cs`'s `NormalizeGradientStops` already resolves
each stop's true fractional position correctly (including when it isn't 0 or 1), and that value
flows untouched into `XLinearGradientBrush._positions`/`XRadialGradientBrush._positions` - the
position *data* was never wrong. The bug was entirely in
`src/PeachPDF/PdfSharpCore/Pdf.Advanced/PdfShading.cs` turning that into a PDF shading dictionary.
The `/Coords` are always the gradient's *full* geometric line (`ComputeGradientLine`, no knowledge
of stop positions), so `/Domain [0 1]` genuinely means "0 = start of the box, 1 = end of the box"
- but every function-construction path assumed the first stop sits at domain 0 and the last at
domain 1, regardless of where they actually were:

- Exactly-2-color gradients (`SetupFromBrush(XLinearGradientBrush,...)`, `SetupRadialMultiStop`,
  and their two `*AlphaExtGStateIfNeeded` soft-mask mirrors) took a shortcut: a single
  `/FunctionType 2` interpolating C0→C1 across the *whole* `[0,1]` domain, discarding
  `_positions` completely.
- 3+-color gradients (`BuildStitchingFunction`/`BuildAlphaStitchingFunction`) built `/Bounds` only
  from *interior* stops (`positions[1..n-2]`); the first sub-function's domain was implicitly
  `[0, positions[1]]` and the last implicitly `[positions[n-2], 1]` - `positions[0]` and
  `positions[n-1]` were never consulted.

For the Charts.css hairline (both stops at ~2-5% of the tile height) this stretched what should
be a solid-then-instant-transparent step into a smooth fade spanning the *entire* tile - the
"gradient below the line". That wash is painted per-column (the CSS rule targets each data
column's own `tr`, not the whole chart), so the sharp edge where one column's wash met the
transparent gap beside it read as the reported vertical line.

Confirmed by rasterizing the actual showcase PDF with both PDFium and MuPDF before the fix (visible
gray wash bands, and a distinct vertical seam between columns 2 and 3) and after (crisp hairlines,
seam gone, both renderers agree) - see [Testing conventions](../../CLAUDE.md#testing-conventions).

## Load-bearing idea

**A gradient's colors must hold solid outside its first/last stop position (CSS Images 4 §3.5.5)
- the PDF representation has to say so explicitly, because the shading's `/Domain` is anchored to
the geometric gradient line, not to the stops.** Fixed by generalizing the segment model
`BuildStitchingFunction`/`BuildAlphaStitchingFunction` already used for interior stops to also
cover the two ends:

- New `BuildStitchSegments(double[] positions)`: splits the domain into an optional leading
  constant-`colors[0]` pad (`[0, positions[0]]`, only when `positions[0] > 0`), one interpolating
  segment per adjacent real-stop pair, and an optional trailing constant-`colors[^1]` pad
  (`[positions[^1], 1]`, only when `positions[^1] < 1`). A pad segment is just an interpolating
  segment whose `LeftIndex == RightIndex` - both builders read `colors[seg.LeftIndex]`/
  `colors[seg.RightIndex]` uniformly, no separate pad-vs-interpolate branch needed downstream.
- `RequiresStitchingFunction(positions)`: true when there are more than 2 stops, or the first/last
  isn't already at the domain edge (small epsilon). The four call sites that used to gate on
  `colors.Length > 2` now gate on this instead - when `positions == [0, 1]` exactly (the common
  case: no explicit stop positions, or the classic `linear-gradient(red, blue)`), this is `false`
  and the cheap plain `/FunctionType 2` shortcut is kept, unchanged from before. This was a
  deliberate choice over always using the stitching function: avoids PDF size bloat for the
  overwhelmingly common case, and keeps the fix's behavioral footprint limited to gradients that
  were actually wrong.

## What was deliberately not done, and why

- No changes to `CssImagePainter.cs`, `BackgroundLayerResolver.cs`, or
  `BackgroundImageDrawHandler.cs` - the `background-size`/`-position`/`-repeat` tiling machinery and
  the stop-position *resolution* were already correct; only the PDF function construction from
  already-correct positions was wrong.
- Conic gradients (`BuildConicMeshData`) are unaffected - a Type 4 mesh is built from explicit
  per-vertex angles, not a stitched `/Domain [0,1]` function, so this class of bug doesn't apply
  to them.

## Evidence

- New tests in `LinearGradientIntegrationTests.cs`/`RadialGradientIntegrationTests.cs`: the
  Charts.css hairline shape itself (two stops at the same non-edge position), an ordinary
  `red 20%, blue 80%` (asserting the actual `/Bounds` values, not just "a `/FunctionType` is
  present somewhere" - matching this repo's existing `HardStopShorthand_ExpandsToTwoStops`
  convention), a regression guard proving the default-edges case still emits the cheap
  `/FunctionType 2` (no `/FunctionType 3`), and a combined color+alpha case proving both
  `BuildStitchingFunction` and `BuildAlphaStitchingFunction` got the fix.
- Rasterized the regenerated Charts.css showcase's "Columns" chart with both PDFium and MuPDF;
  gridlines are crisp with no wash and no vertical seam in either, matching a browser rendering of
  the same HTML.
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`: 9883 passed, 9
  pre-existing platform-specific skips, 0 failed (full suite).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors.
- `diff-cover` against `main`: 100% diff coverage on the changed lines.
