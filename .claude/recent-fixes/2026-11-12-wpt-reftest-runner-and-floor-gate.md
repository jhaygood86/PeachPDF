# WPT reftest runner (`src/harness/PeachBrackets.WptRunner`) with a per-module pass-count floor gate

Evaluated porting onerender-csharp's web-platform-tests setup. Its WPT *tests* are not vendored: it fetches a pinned upstream
commit and keeps only a runner and results. We wrote our own runner (no code copied, onerender used for design ideas: outcome
set with a `blank` guard, floors that only rise, fuzzy/chain rules) against the public `PeachBrackets` API.

## Load-bearing choices
- **Seam**: `HtmlDocument.LoadAsync` as `MediaType.Screen`, `PageWidthPoints = 600`, `ViewportHeightPoints = 450`, margins 0, painted onto
  `RasterRenderContext.CreateCanvas(800, 600)` with a `Matrix3x2.CreateScale(4/3)` pushed (layout unit is the point; 1 CSS px = 1 device px).
- **Declaring the surface as 4/3 pixels per point instead** (`new RasterSurface(.., 4/3, 4/3)` + `new RasterCanvas(ctx, surface, 4/3)`) was
  tried and measured: css2-flow 38% -> 29%, larger edge differences. Kept the transform. Turning on `SnapBoxDecorationsToCssPixels`/
  `SnapBorderWidthsToCssPixels` changed nothing measurable but matches what the references assume, so it stays on.
- **Base URL**: a `RasterRenderContext` is sealed with no base URI, and the engine resolves `<link rel=stylesheet>` and `@import` *before*
  it knows the document's `<base>` (see the comment on `HtmlContainerInt.DocumentBaseUri`). So the runner injects `<base>` for images and
  rewrites stylesheet links and `@import` URLs to absolute `http://wpt.test/...` addresses its `ResourceLoader` serves from the checkout.
  Without that, 100+ tests died with "This operation is not supported for a relative URI" (21% -> 35% on a css2-flow sample).
  This is an engine gap a standalone consumer hits; not recorded as an accepted gap because it has no tracking issue yet.
- `.xht`: only the XML declaration and CDATA markers are rewritten; no XML parser. Print reftests are not run yet.
- Errors name the root cause (`error: HtmlRenderException: ... <- InvalidOperationException: ...`); the engine wrapper alone hid it.

## Numbers (Windows, first run, WPT ffc4686d)
css2-flow 871/2415 pass, css-flexbox 324/1398, css-grid 231/2482, css-transforms 427/905; whole run ~45 s, identical at `--parallel 3` and the
default (deterministic). Spot-checked rasterized pairs: bidi-001 (a split inline box's border drawn wrong) was a real engine bug, fixed in [2026-11-12-a-bidi-split-inline-box-is-drawn-as-separate-pieces](2026-11-12-a-bidi-split-inline-box-is-drawn-as-separate-pieces.md); margin-bottom-applies-to-014 is NOT an engine bug - its `rel=match` names the reference of the `display:block` test (two empty full-width bars), which no conforming engine can match an `inline-table` with a 200px cell against; margin-009 looks identical but differs by AA fringes (max 28) - that is the
runner's noise floor for overlapped edges, not investigated further.

## Gate
`wpt/wpt-floors.json` was seeded at 95% of the Windows pass counts to absorb platform font differences (the Linux CI run could not be
measured locally: no .NET in WSL). `--update-floors` only raises, so the first CI run's numbers should be committed to tighten them.
`.github/workflows/wpt.yml` runs on PRs touching `src/engine/PeachBrackets`, `src/drawing` or the runner. Verified a floor above the
measured count exits 1 with `REGRESSION: ...`.

## Not done
Print reftests, XML parsing for `.xht`, Chrome calibration, modules beyond the 16, a test project for the runner's parser.
