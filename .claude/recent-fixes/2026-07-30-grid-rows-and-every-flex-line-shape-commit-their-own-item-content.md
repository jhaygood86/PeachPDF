# Grid rows and every remaining flex line shape commit their own item content

Closes #517 (stage 4 of #390) and #526 in full. #430/PR #527 (same day, see
`2026-07-30-a-flex-lines-item-content-fragments-through-a-real-commit-pass.md`) gave
`CssLayoutEngineFlex` a live commit pass for exactly one shape: a single-line, row/row-reverse
container. This closes the three shapes that PR deliberately left out — grid (every row, including
row-spanning and `subgrid` items), flex multi-line (`flex-wrap`), and flex column-direction — plus
#455's content-fragmentation half for column-direction (its break-*value* half stays open, see
below).

## The load-bearing idea

Grid and flex-multiline generalize directly: both already have a whole-line/row relocation pass
(`RelocateRowsAcrossFragmentainers` / `RelocateLinesAcrossFragmentainers`) that runs before the
commit pass and settles every item's final position, and — the fact that unblocked this — a grid
row's or flex line's *cross-axis* size is fixed from an unfragmented measurement pass that also
runs before the commit pass. A spanning item's content fragmenting live during commit therefore
cannot perturb track/line sizing; it only decides where the item's content *draws*, not how much
room it was given. The one hazard initial research flagged (row-track growth reacting to a
commit-time height change) turns out not to apply, once traced.

Flex column-direction needed a genuinely different shape rather than a generalization: its items
are a *sequential* flow (each is its own potential break point), not the row/row-reverse "parallel
flows" `FlexBreakToken`/`GridBreakToken` model — so `FlexColumnBreakToken`/`ColumnLineCursor`
mirror `BlockBreakToken`'s index-into-a-list shape instead, two-level because a `flex-wrap` column
container can have several lines running side by side, each its own independent sequential run.

Both new engines reuse `ItemContentCommit.CommitLayout` (extracted, unchanged, from flex's own
existing `PerformCommitLayout`/`PerformLayoutBlockifiedAtFinalPosition`) rather than duplicating
it — nothing in either method was ever flex-specific.

## What was found only by running it

- **Grid nested in a multicolumn container silently dropped content**
  (`AGridChildOfAMulticol_KeepsItsContentInsideTheColumn`, an existing test, regressed). A resumed
  pass landing in a new fragmentainer moves the container itself
  (`CssBox.ResumeInTheNextFragmentainer`) but — by that method's own doc comment — moves *only*
  that box, not its subtree, because an ordinary continuing child gets a fresh `Location` from
  `PlaceBlockChild` every pass. Grid's (and flex's) resume short-circuit skips that path entirely,
  so nothing repositioned the not-yet-committed items to the new column; their continuation content
  landed at the stale column's X, outside the new fragmentainer's region, and the emitter silently
  dropped it. Fixed by threading a `PlacementOrigin` snapshot through both break-token types and
  reapplying the container's own delta to every remaining item on resume
  (`ItemContentCommit.RepositionForResume` — a direct `Location` reassignment, not
  `OffsetLeft`/`OffsetTop`, so an item with content already frozen in the fragmentainer being left
  is not disturbed). This gap was latent in the original single-line flex commit pass too, just
  never exercised by an existing test; the fix applies to both engines.
- **A resumed flex commit pass silently no-opped** once the commit loop gained a shared gate that
  read `_isRow`. That field is set by `ParseFlexDirection()`, which `Layout`'s resume short-circuit
  deliberately never calls (a fresh `CssLayoutEngineFlex` instance backs every `PerformLayout`
  call), so it silently defaulted to `false` on every resumed pass, and the gate returned before
  doing anything. Fixed by moving the `_isRow` check to the fresh call site only, which is the only
  place it is actually initialized — a resumed pass never needs it, since only the row-direction
  path ever publishes a `FlexBreakToken` to resume in the first place.
- **The existing `StraddlingLineClaimTests` flex/grid cases pinned exactly the symptom this
  closes.** Both were regressed deliberately (not silently) as each shape's commit pass landed,
  and replaced with tests asserting the new behavior — content now genuinely continues instead of
  straddling. Only `MonolithicContent.FitsNoFragmentainer` (unrelated to any engine) still leaves a
  line straddling a page boundary.

## What was deliberately not done

A forced `break-before`/`break-after`/`break-inside: avoid` **between two items of the same
column-direction line** is still inert — every item commits unconditionally, stopping a line's
walk only where an item's own content genuinely does not fit. This is the remaining half of issue
#455 (`.claude/accepted-gaps/flex-column-container-has-no-break-points-between-items.md`, left
unedited and still open): replicating ordinary block flow's forced/avoided-break decision machinery
for a unit that is not `CssBox.Boxes` children of an ordinary block is a separate design surface
from content fragmentation, and attempting it inline here would have been exactly the hand-waving
this repo's own conventions ask against.

## What was measured

- Full suite: 6999 passing (net8.0), zero failures, after each phase's own regressions were found
  and fixed.
- Three new dimensions of dedicated tests: grid (multi-row continuation, a row-spanning item's own
  content, a `subgrid` item's adopted columns surviving a resumed pass), flex multi-line (a later
  line continuing, `wrap-reverse` block-axis ordering), flex column-direction (a later item
  continuing, independent fragmentation of two side-by-side wrapped lines, `column-reverse`
  block-axis ordering).
- Three new showcases (`css_grid_fragmentation`, `flex_multiline_fragmentation`,
  `flex_column_fragmentation`), each rasterized with both PDFium and MuPDF: identical page counts
  and content between renderers, continuation content genuinely present on the second page rather
  than lost or duplicated.
- `dotnet build PeachPDF.slnx -t:Rebuild`: zero warnings across the whole solution.
