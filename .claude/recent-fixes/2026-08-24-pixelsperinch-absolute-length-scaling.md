# Absolute CSS lengths render wrong under non-default `PixelsPerInch` (#814)

## Load-bearing idea

`PdfGenerateConfig.PixelsPerInch` (default 72) sets `PdfSharpAdapter.PixelsPerPoint =
PixelsPerInch / 72`, and the public `HtmlContainer` wrapper inflates `HtmlContainerInt`'s entire internal
layout coordinate space by that factor relative to real PDF points; `GraphicsAdapter` divides every
painted coordinate back down by the same factor at paint time. Percentage/content-relative sizing and
text (`FontAdapter`'s own compensating divide/multiply) already round-trip through that inflate-then-
divide correctly. Absolute CSS lengths (`px`/`pt`/`in`/`cm`/`mm`/`pc`) did not — nothing compensated them,
so at `PixelsPerInch=96` (`PixelsPerPoint=1.333`) a `34px` box painted at `34px * 0.75 / 1.333 ≈ 19.1pt`
instead of the spec-correct `25.5pt`. This went unnoticed because every existing test hardcoded
`PixelsPerPoint = 1.0` (the identity case), making the bug a no-op everywhere it was exercised.

Fix: multiply an absolute length's resolved value by the ambient `PixelsPerPoint` exactly once, only at
the box-aware `CssValueParser.ParseLength` overloads (gated on `Length.IsAbsolute`) and the analogous
`CssLayoutEngine.TryResolveAbsolute`/`MeasureIntrinsicSize` call sites — never by changing
`Length.ToPixel()` itself, since several existing callers (`DomParser`'s `@page` subsystem,
`MarginBoxRenderer`) deliberately want the raw, unscaled true-point result and already apply their own
correct external multiply at their own paint/margin-box boundary.

## What running it (not reading it) found

- **`PdfSharpAdapter.PixelsPerPoint` defaulted to `72d`, not `1.0`.** Harmless before this fix (nothing
  read it meaningfully), catastrophic once absolute-length code started consulting it — every
  bare-constructed `new PdfSharpAdapter()` in the test suite (the overwhelming majority) suddenly
  multiplied every absolute length by 72x. Fixed the default; every bare-constructed test adapter now
  needs `PixelsPerPoint = 1.0` set explicitly where the test cares about absolute lengths at all (most
  don't, since 1.0 is now the default).
- **`XFont.Height`'s `(int)Math.Ceiling(...)` truncation** (`PdfSharpCore/Drawing/XFont.cs`) collapsed to
  exactly `1` for any sub-1pt font — which the corrected absolute-length code now legitimately constructs
  at large `PixelsPerPoint` values, since font size itself is resolved through the same inflated/deflated
  space. `FontAdapter.Height`/`Ascent` then multiplied that truncated `1` back up by `PixelsPerPoint`,
  landing exactly on `PixelsPerPoint` itself instead of the true line height — silently wrong line heights
  for ~28 pagination tests that had been asserting against this bug's own output. Fixed by switching
  `FontAdapter` to `font.GetHeight()` (double precision, no truncation) and moving `Math.Round` to the
  property getters, applied after multiplying by `PixelsPerPoint` rather than before.
- **`CssLayoutEngineTable.CanReduceWidth`'s inverted bounds check** (`_columnWidths!.Length >=
  columnIndex`, always true for a valid index) makes `ShrinkColumnsToFitAvailableWidth` provably dead
  code today. The corrected font-metric fix above changed a table max-width test's actual column-width
  arithmetic enough to nearly trip this — investigated fixing the bounds check directly, but doing so in
  isolation activates the shrink path for *every* table and breaks vertical-writing-mode column sizing,
  plus exposes a second, unbounded inner loop (`while (!CanReduceWidth(curCol)) curCol++;` never wraps).
  **Deliberately reverted** rather than fixed — the real fix needs a dedicated investigation of the
  vertical-writing-mode interaction, out of scope here. Tracked as
  [issue #819](https://github.com/jhaygood86/PeachPDF/issues/819); see
  [.claude/accepted-gaps/table-shrink-columns-dead-code.md](../accepted-gaps/table-shrink-columns-dead-code.md).
- **The actual, most direct #814 symptom — an inline `<svg>` icon clipped by its own bounding box — was
  NOT fixed by the layout-level corrections above.** Confirmed only by generating a real PDF from the
  issue's repro HTML and rasterizing it with both PDFium and MuPDF (per this repo's two-renderer paint-
  verification convention) — the layout-level fix alone still clipped the icon. Root-caused with temporary
  debug instrumentation to `GraphicsAdapter.PushTransform` only ever dividing a transform matrix's
  translation (`OffsetX`/`OffsetY`) by `PixelsPerPoint`, never its linear/scale part (`M11`/`M12`/`M21`/
  `M22`), while `SvgRenderer.ComputeViewportTransform`'s scale is `viewportRect.Width / viewBoxWidth` — an
  inflated numerator over a never-inflated (dimensionless SVG user-unit) denominator, so the resulting
  scale came out `PixelsPerPoint` times too large. `g.PushClip` on the same viewport rect divides by
  `PixelsPerPoint` correctly (clip is a plain rect, not a matrix), so the clip and the content transform
  disagreed by exactly `PixelsPerPoint`. Fixed with a new `SvgRenderer.ComputePaintViewportTransform`
  helper that pre-divides only the transform's linear part before painting (the original
  `ComputeViewportTransform` is untouched and still used as-is for link-annotation geometry and
  marker-space math, which are correct as they are) and a new `RGraphics.PixelsPerPoint` virtual property
  (default `1.0`, overridden by `GraphicsAdapter`) so `SvgRenderer` can read the ambient scale without a
  new parameter threaded through every paint call.
- **Internal round-trip serialization landmine**: `CssLayoutEngineFlex`/`Grid`/`ItemContentCommit`/
  `CssLayoutEngineTable` (caption) temporarily reformat an already-resolved layout value back into
  `box.Width`/`Height` as a `"Npt"` string to re-run layout through the ordinary parse path (see the older
  `5864f88f` fix for a related px-vs-pt bug in the same mechanism). Naively making `ParseLength` ppp-aware
  double-scaled every one of these round-trips; fixed by making the shared `FormatLayoutUnits` helper
  pre-divide by `PixelsPerPoint` before formatting, so re-parsing multiplies it right back to the original
  value.
- Unitless `line-height` (`CssValueParser.ParseLength(LengthOrUnitless, ...)`) needed the same
  `PixelsPerPoint` scaling as absolute lengths, since it's computed as a multiple of `GetEmHeight()` in the
  same inflated space — added directly to `DerivedStyle.ActualLineHeight`'s default-branch computation too.
- **A page-break boundary test (`CssLayoutEngineTablePageBreakTests.AvailableHeight_PageBreakFiringPoint_RowDoesNotBleedIntoBottomMargin`) failed only on the `ubuntu-latest`/`macos-latest` CI runners**, never `windows-latest` — a genuine cross-platform floating-point epsilon (`125.00000000000001` vs. an exact `125` boundary the fixture is deliberately calibrated to land on), not a logic bug; the font-metric precision fix above is exactly the kind of change that can move a value by a platform-dependent ulp. Fixed by padding the test's own boundary comparison with a `1e-6` tolerance.
- **A second, more severe CI-only hang, found only by actually running the suite under WSL/Linux** (the Windows runner passed cleanly; `dotnet test` under WSL reproduced it, and `dotnet-dump collect`/`analyze` on the stuck process pinned the exact stack): `TableRepeatedGroupConditionsTests.AGroupExactlyAQuarterOfThePage_DoesNotRepeat` was grinding toward `HtmlContainerInt`'s 100,000-pass fragmentation cap (~20 minutes) instead of failing fast or completing in a handful of pages. Root cause: `CssLayoutEngineTable.DetachAndMeasureRepeatedRowGroups`'s one-shot header/footer measurement pass — meant to measure a `<thead>`/`<tfoot>` at a provisional position, independent of where it will finally repeat — ran with the container's real, live `CurrentFragmentainer` still in scope. `CreateLineBoxes`'s own page-break check (`word.WouldStraddleFragmentainer`) saw the measurement's provisional position sitting in whatever page band happened to be live and deferred the group's first line to "the next fragmentainer" — which a one-shot measurement never revisits, so the group measured a height of exactly `0`. `SettleWhetherTheGroupsRepeat` then read that `0` as trivially "inferior to" any quarter-page cap, letting the group repeat with **no room actually reserved for it**, and every subsequent band — still unable to fit even one line in what little was left — needed a band of its own. The font-metric precision fix above is what moved this specific fixture's numbers close enough to the boundary to trip it; the underlying defect is unrelated to #814 and was already reachable by any real document pairing a small custom page size with a repeated header/footer. Fixed by wrapping the header/footer measurement with `HtmlContainerInt.DetachFragmentainer()`/`RestoreFragmentainer()` plus `SuppressWordPageBreaks`, mirroring the identical "measured at a provisional position" fix already used by `RunningElementLayout.LayoutRunningElementFor` and `CssLayoutEngineFlex`/`Grid`'s own item-measurement passes. A hand-tuned regression test for this (`AGroupNarrowerThanTheBandItRepeatsIn_StillMeasuresItsRealHeight`, band ≈ 98% of the group's own height) reproduced the defect reliably when run *alone* but became order-dependent — passing or hanging depending on which other tests in the file ran first, almost certainly `PeachPDF.Fonts.FontFactory`'s process-wide static font cache (see this repo's own `CLAUDE.md` warning about exactly this) shifting the measured height by enough to cross the empirically narrow (~95–98%) trigger band. Removed rather than landed flaky; the pre-existing, unmodified `AGroupExactlyAQuarterOfThePage_DoesNotRepeat` already covers this defect and was confirmed stable (passing, in well under a second) across many repeated full-suite and whole-file runs on both platforms after the fix.

## Deliberately not done

- `MediaQueryMatcher.CompareLength`/`ContainerQueryMatcher` (absolute-length media/container-query
  features, e.g. `(min-width: 600px)`) have the identical bug — confirmed by reading `MediaQueryContext`'s
  `ViewportWidthPt` source (`HtmlContainerInt.PageSize`, inflated space despite the `Pt` naming) — but
  `CompareLength` has no `CssBox`/adapter in scope to consult for `PixelsPerPoint`, so fixing it means
  threading a new field through `MediaQueryContext`/`ContainerQueryContext`. Left out of this PR to keep
  it bounded; tracked as [issue #820](https://github.com/jhaygood86/PeachPDF/issues/820), see
  [.claude/accepted-gaps/media-container-query-absolute-length-pixelsperpoint.md](../accepted-gaps/media-container-query-absolute-length-pixelsperpoint.md).
- `CssImagePainter`'s explicit gradient radius (`radial-gradient(50px at ..., ...)`) and `ConvertLength`'s
  absolute-length branch (conic/radial gradient stop positions) resolve via the bare `Length.ToPixel()`
  with no box/adapter in scope either — same bug class, same reason for deferring. Tracked as
  [issue #821](https://github.com/jhaygood86/PeachPDF/issues/821), see
  [.claude/accepted-gaps/gradient-absolute-radius-pixelsperpoint.md](../accepted-gaps/gradient-absolute-radius-pixelsperpoint.md).
- CSS `transform: translate(50px, ...)` turned out to **not** need a separate fix: it was suspected during
  planning as the same bug class (a dead-code `CSS/Values/ITransform`/`TranslateTransform` hierarchy has
  its own unscaled `.ToPixel()` calls), but the actual, live `transform` paint path
  (`CssValueParser.ParseTransform`/`BuildFunctionMatrix`) already resolves its length arguments through
  the same box-aware `ParseLength(string, hundredPercent, box)` overload this fix corrected — confirmed by
  reading `DerivedStyle.ActualTransformMatrix`'s call chain, not by assumption. No change needed; the
  `ITransform` hierarchy is unrelated dead code, out of scope to remove here.

## Evidence

- New permanent regression suite:
  `src/PeachPDF.Tests/Integration/PixelsPerInchAbsoluteSizingIntegrationTests.cs` (img/svg intrinsic
  sizing, a structural svg-clip-vs-content-transform adjacency assertion rather than a magic-number
  comparison, flex items, plain absolute-sized divs, and an absolute-inside-percentage nesting case) — all
  passing.
- Full suite: `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` — 9196 passed, 0
  failed, 9 skipped (pre-existing OS-specific skips) — confirmed on both Windows (net8.0) and WSL/Linux
  (net10.0, no net8.0 runtime available there), the latter run twice in full to confirm the fragmentation
  hang fix wasn't a one-off.
- Visual verification: generated a PDF from the issue's exact repro HTML at `PixelsPerInch=96`, rasterized
  with both PDFium (`pypdfium2`) and MuPDF (`PyMuPDF`) — the SVG icon renders fully unclipped and at the
  correct size in both, where before the fix PDFium's lenient bitmap render hid the clip that Foxit/
  Chrome's stricter PDF viewers showed.
- `dotnet build PeachPDF.slnx -t:Rebuild` — zero warnings.
