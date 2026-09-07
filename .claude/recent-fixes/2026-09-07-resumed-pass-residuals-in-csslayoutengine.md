# Resumed-pass residuals in CssLayoutEngine.FlowBox (#341, #342, #343)

Closes #341, #342, #343. (#340 investigated and closed separately as already-resolved — see below.)

## What was wrong

Three residuals left by earlier fixes (#336 and predecessors) to `CssLayoutEngine.FlowBox`'s
one-pass-per-fragmentainer, resumable inline layout:

1. **#341** — `PrepareFlowBoxEntry` calls `CssNamedStringEngine.ApplyStringSet(box)` for a `string-set`
   box, which seeds `NamedString.Y` from `box.Location.Y` — meaningless for a plain inline box (only its
   words are ever positioned, never the box itself). `FinalizeFlowBoxExit`'s own later correction is
   gated on `opensHere`, but a box whose own content straddles a fragmentainer boundary never reaches
   `FinalizeFlowBoxExit` at all on the pass that opens it: `FlowBox`'s recursive call returns as soon as
   `coordinates.Break` is set, before its own exit bookkeeping runs — and on the pass that finally
   completes the box, `opensHere` is false (it already opened earlier), so that gate never fires either.
   The value was left at the meaningless seed forever, corrupting `MarginBoxRenderer.ResolveNamedString`'s
   page attribution for `string(name, first/start/...)`.
2. **#342** — `BubbleRectangles` compensated for a box whose leading spacing (border+padding) was applied
   on the line it was entered on, when that box's own first word then wrapped to a new line — by
   subtracting the box's full `margin + border + padding` from the rectangle. This double-counted margin
   (the cursor already excludes margin from `word.Left`) and, more fundamentally, was compensating for a
   stale `CssBox.FirstHostingLineBox` (stamped at the box's entry, before the wrap was known) rather than
   correcting it.
3. **#343** — `ApplyAtomicInlineVerticalInsets` (an inline-block's top/bottom border+padding, applied by
   shifting the words it flows) only ever applied once, matching `box-decoration-break: slice` — under
   `clone` (css-break-3 §6.2), every fragment of a straddling inline-block should re-open with its own top
   border/padding and re-close with its own bottom border/padding, and this never happened for a
   continuation page at all.

`#340`'s own described mechanism (`CreateLineBoxes`'s `if (blockBox.ActualRight >= 90999)` branch, fed by
`CssLineBoxCoordinates.MaxRight`) was investigated and found **unreachable in current code**: instrumenting
that branch and running the full suite (9897 tests) never triggered it once. Every shrink-to-fit width
resolution path checked (`float`, `table-cell`, absolutely-positioned auto-width) now resolves width via
`CssLayoutEngine.GetFitContentWidth`/`GetBoxWidth`'s own intrinsic-width measurement *before* `CreateLineBoxes`
runs, not via this runtime accumulation. No fix was made; #340 is closed as already-superseded.

## The fix

1. **#341**: right after `ApplyStringSet(box)` in `PrepareFlowBoxEntry`, stamp
   `namedString.Y = coordinates.CurrentY` for every named string the box just registered — the flow's own
   cursor at the exact moment the box opens, always correct regardless of how far the box's content later
   continues.
2. **#342**: correct `CssBox.FirstHostingLineBox` at the wrap site itself (`FlowBox`'s wrap branch), for
   every inline ancestor whose content genuinely begins with the wrapping word — the wrapping box itself
   (self-iteration) or, transitively, every ancestor that is in turn its own parent's first child (a new
   `IsFirstChildOfItsParent` helper). `CssLineBox.UpdateRectangle`'s own pre-existing leading-spacing
   subtraction (keyed on this same field) then fires correctly on its own; the old `BubbleRectangles`
   compensation was deleted. A second gap surfaced by code review: when the wrapping box (`b`) is not the
   corrected ancestor itself (e.g. `b` is an anonymous run box holding an ancestor span's words), only
   `b`'s own leading spacing was ever reserved on the cursor — the ancestor's real spacing was never added
   at all. The fix now also adds each corrected ancestor's own margin/border/padding-left to the cursor at
   the wrap, mirroring what `FlowBox`'s normal `childOpensHere` path would have added had the wrap not
   pre-empted it. The same cascade also re-stamps `NamedStrings.Y` for a `string-set` ancestor, mirroring
   #341's entry-side fix for the "own first word wraps" case.
3. **#343**: a new `clonesAtomicDecorations` local widens the top-inset pre-shift condition from
   `childOpensHere` alone to `childOpensHere || clonesAtomicDecorations`, and adds
   `ApplyAtomicInlineVerticalInsets(b, coordinates, atomicBottomInset)` calls (gated on
   `clonesAtomicDecorations`) at both existing `if (coordinates.Break is not null) return;` sites, which
   previously returned without ever applying the bottom inset. A pass that merely walks through a box
   already fully finished earlier takes the same widened pre-shift branch but places nothing new (every
   word ordinal is skipped), so `coordinates.Line` never changes and the shift is cancelled by the existing
   post-dispatch "undo" with no effect — verified by tracing the ordinal-skip path, not just asserted.

## What was found by running it, not by reading it

- #340's own branch was confirmed dead by instrumentation (a file-append probe on
  `blockBox.ActualRight >= 90999`) across the full 9897-test suite — zero hits. Reading the code alone
  suggested the branch was live; running it proved otherwise.
- The #342 fix's first version (correcting only `FirstHostingLineBox`, matching the issue's own literal
  description) passed the full suite but was **incomplete** — code review (two independent agents, one
  verifying empirically) found and reproduced a case (`width:25pt`, span padding, anonymous run box holding
  the words) where the rectangle still computed outside the block, because the ancestor's own leading
  spacing was never added to the cursor. Fixed by extending the same cascade to also add each corrected
  ancestor's own spacing.
- The naive "widen `IsFirstChildOfItsParent`'s cascade to also start from `b.ParentBox` when `b != box`"
  first attempt broke the pre-existing `Slice_BreakFallingAtAnInlinesFirstWord_PadsItOnce` regression test;
  the correct cascade starts from `box` (the box whose own `FlowBox` recursion this is), not `b.ParentBox`,
  with `ReferenceEquals(b, box)` as the alternate self-iteration entry condition.

## Evidence

- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`: 9903 passed, 0 failed, 9 skipped
  (pre-existing platform-gated skips).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors.
- Diff coverage (`diff-cover` against `main`): 91.2% on the one changed production file (three lines in a
  narrow branch — the first break-return site's clone-bottom-inset call for a box holding words directly —
  not exercised by the fixtures used; above the 90% gate).
- New/updated tests: `ResumedInlineNamedStringLayoutIntegrationTests.cs` (straddling and same-pass-wrap
  `string-set` position tests), `ResumedInlineDecorationLayoutIntegrationTests.cs` (rectangle-inside-block
  tests for both the self-iteration and anonymous-run-box wrap shapes, and the `clone` top-inset-repeats
  test) — each confirmed to fail against the pre-fix code before being confirmed passing against the fix.
