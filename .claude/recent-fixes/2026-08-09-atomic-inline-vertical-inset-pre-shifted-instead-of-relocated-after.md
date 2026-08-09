# Atomic-inline vertical inset pre-shifted instead of relocated after

_Landed 2026-08-09._

[Issue #333](https://github.com/jhaygood86/PeachPDF/issues/333): `CssLayoutEngine.FlowBox`'s
`ApplyAtomicInlineVerticalInsets`/`OffsetFlowedWords` shifted an atomic inline-level box's
(`display:inline-block`) already-placed words down by its own border-top+padding-top (CSS2.1 §8.1),
then re-ran the pre-#321 per-word `CssRect.BreakPage()` relocation on each shifted word - *after* the
line's break-token decision had already been made, so the shift could silently push a line back across
a fragmentainer boundary the token model had just decided was fine.

**Fixed by pre-shifting instead of correcting after the fact.** `FlowBox`'s per-child loop now shifts
`coordinates.CurrentY` by the child's own top inset *before* flowing its content (gated on
`childOpensHere`, so a continuation resuming in a later fragmentainer never re-pays a padding it already
paid on an earlier one), restoring it afterward only if the child's content stayed on the line it
started on (detected by comparing `coordinates.Line` by reference - an inline-block's content can wrap
onto more than one internal line inside the same recursive call, and a wrapped later line's `CurrentY`
already derives correctly from the pre-shifted `MaxBottom`, not from re-adding the shift). This lets the
existing, unmodified per-word `WouldStraddleFragmentainer` check make the correct call the first time,
for every internal line, not just the first - `CreateLineBoxes` can only discard one in-progress line at
a time, so a shift-then-recheck design (discovering the straddle only after the whole subtree had
already been placed) could never have handled a straddle on a line *other* than the last one.

**The second call site the issue's own follow-up comments flagged as blocking full closure turned out to
already be resolved.** `FlowBox`'s own `else { word.BreakPage(); }` arm (taken when
`coordinates.Fragmentainer` is null) was, at the time #333 was filed, the only mechanism that paginated a
table cell's text - the table engine ran behind a detached fragmentainer. That changed with #464 (closed
2026-07-30, part of the #390 epic): `CssBox.LayoutContents`'s table arm no longer runs behind a detached
fragmentainer, and cell content now sees a live one like everything else. Verified this made the `else`
arm dead by running the full suite after removing it entirely (8418 passing, including every
`TableCellLineFragmentationTests` case and `StraddlingImage_MovesWholeThroughTheWordPath`, whose own
comment had claimed it still needed the legacy relocation - that comment predated the migration and was
simply stale). This let `CssRect.BreakPage`/`WouldStraddleItsOwnBand` be deleted outright (exactly two
production callers, both retired here) along with `CssBox.BreakPage`/`CssSpacingBox.BreakPage` (zero
production callers - only three now-removed unit tests exercised it directly).

**A real double-counting bug surfaced during review, not by reading the diff but by A/B-testing it against
`main`.** `FlowBox.FinalizeFlowBoxExit`'s "handle height setting" fallback (extends a block's `MaxBottom`
to cover an atomic inline box's own declared height when its flowed content came out shorter, e.g. a
button whose padding exceeds one small text line) anchors the correction on `startY`, captured at the
*callee's own* entry - which, for a box reached through the recursive branch, is now the pre-shifted
content-box top rather than the border-box top the fallback's `ActualHeight` term assumes. Left alone,
the top inset would be counted twice for exactly the boxes this whole fix targets. Fixed by reconstructing
the true border-box top (`startY` minus the same inset, recomputed from the box's own properties) before
either of `FinalizeFlowBoxExit`'s two fallbacks (height and the analogous width/rect one) use it. Neither
the pre-existing test (a `>=` lower bound, which an over-large height still satisfies) nor this fix's own
first pair of new tests (word position only) would have caught it - closed with a new test asserting the
block's height against an exact expected value, confirmed to fail without the `trueStartY` correction.

Tests: two new multi-column tests in `InlineBlockHeightRegressionTests.cs` pin the fragmentainer case
specifically - a single-line inline-block whose padding-top alone crosses a column boundary (whole line
moves, and since nothing was kept anywhere the resumed pass re-applies the inset once), and a two-internal-line
box (via forced `<br>`) where only the second line straddles and moves alone *without* re-applying the
inset (a genuine continuation, not a fresh opening of the box's content) - plus a tight height-equality
test pinning the `FinalizeFlowBoxExit` fix. `orphans:1; widows:1` is set explicitly in the two-line
fixture to isolate the mechanism under test from the UA default orphans/widows relaxation (which would
otherwise refuse to leave a single orphan line behind and move the whole two-line box - a different,
already-tested mechanism). `column-fill: auto` is set to get sequential column filling rather than the
default balance, which redistributes content across columns even when it would all fit in one. Full net8.0
suite: 8419 passing, 9 skipped (pre-existing). CLI: 96/96. 100% diff coverage. `dotnet build
PeachPDF.slnx -t:Rebuild`: 0 warnings.
