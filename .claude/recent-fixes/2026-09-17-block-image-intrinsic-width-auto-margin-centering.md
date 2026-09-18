# A `display: block` image/SVG with no declared width now centers correctly with `margin: auto`

`.claude/recent-fixes/2026-09-17-block-image-text-align-centering.md` (the #1176 fix) fixed
`ResolveAutoHorizontalMargin`'s `IsReplacedBlockWrapper` branch for a `display: block` `<img>`/`<svg>`
with a *declared* `width`, but deliberately left the intrinsic-size case broken (see that entry's own
"Deliberately not done" section and the accepted-gap file it pointed to). This closes that remaining
half (issue #1178).

## The load-bearing idea

`ResolveAutoHorizontalMargin`'s `IsReplacedBlockWrapper` branch falls back to `box.FirstWord.Width` when
there's no declared `width` to read directly. The bug was never in *what* it reads — `FirstWord.Width`
genuinely is the box's fully-resolved used width once `CssLayoutEngine.MeasureIntrinsicSize` has run
(it already folds in `max-width`/`min-width`/`aspect-ratio`, the same clamp this branch otherwise
hand-rolls for a declared width) — it was *when*. `CssLayoutEngine.FlowBox` computes `leftSpacing`
(which reads `ActualMarginLeft`, which reaches this method) for each inline child `b`, and only later in
that same loop iteration calls `await b.MeasureWordsSize(g)`, the one place that ever writes
`FirstWord.Width`. So the fallback always read a stale/zero value, regardless of what the image
actually measured to.

`FlowBox` now pre-measures `b` (`await b.MeasureWordsSize(g)`) as soon as `b.ParentBox.IsReplacedBlockWrapper`
is true *and* `b` has no declared CSS width, right before computing `leftSpacing`/`rightSpacing` — before
`ResolveAutoHorizontalMargin` is ever reached for this box. Scoped to the undeclared-width case
specifically (not every `IsReplacedBlockWrapper` child) because a declared width never reaches
`FirstWord.Width` in `ResolveAutoHorizontalMargin` at all — a post-review pass caught the broader guard
needlessly doubling `MeasureWordsSize`'s size-computation work for the declared-width case (#1176's,
already-fixed scenario) on every layout. The original `RectanglesReset()` + `MeasureWordsSize()` call
later in the same iteration is left untouched. This is safe specifically because both
`CssBoxImage.MeasureWordsSize` and `CssBoxSvg.MeasureWordsSize` guard their expensive work (image
load/decode, SVG resource prefetch) behind a `_wordsSizeMeasured` flag, but *always* cheaply rerun the
actual size computation (`MeasureImageSize`/`MeasureIntrinsicSize`) on every call regardless of that
flag — calling it twice per iteration for the undeclared-width case does not re-fetch or re-decode
anything, it just makes the second (already-scheduled) call a no-op on the loading side.
`MeasureIntrinsicSize`'s only external read besides the box's own already-loaded image/SVG
(`ContainingBlock.Size.Width`, for a percentage width) is the ancestor block's size, already resolved
before `FlowBox` starts iterating this block's children at all — not something the reorder puts at risk.

## Deliberately not done

No broader reordering of `FlowBox`'s per-child loop (measuring every child before computing its
spacing) was made, even though the general case looked safe on inspection too — the fix is scoped to
exactly the box shape the bug concerns (`IsReplacedBlockWrapper`'s single child), which is also the only
place `ResolveAutoHorizontalMargin` ever reads `FirstWord.Width` instead of a declared value.

## Evidence

`ReplacedBlockWrapperAlignmentIntegrationTests.cs`: three new cases (a block `<img>` with no declared
width/height centers via its real intrinsic size from a synthesized non-square PNG; the same with
`max-width` narrower than the intrinsic width still centers around the clamped width; an inline `<svg>`
with no declared CSS width centers via its own `width`/`height` attributes, covering the other box kind
`IsReplacedBlockWrapper` wraps). Verified the two `<img>` cases fail without the fix (asserting the
pre-fix offset, which treats the width as 0) and pass with it. Full net8.0 suite: 12,311 passed, 0
failed, 9 platform skips. Diff coverage: the new `FlowBox` branch is exercised (confirmed via the
collected Cobertura report - the guard's body lines were hit). Full solution rebuild: 0 warnings.
Visually verified via the CLI + rasterizing the output with both MuPDF and PDFium: an intrinsically-sized
`<img>` with `margin: auto` renders centered in both renderers.
