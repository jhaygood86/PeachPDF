# PDF outline `/Count` no longer leaks a closed item's descendants into its ancestors

## The load-bearing idea

`PdfOutlineCollection.Add` incremented every ancestor's `OpenCount` whenever the *newly added*
outline had `Opened == true`, walking straight up the `Parent` chain regardless of whether an
intermediate ancestor was itself closed. Per PDF 32000-1 Table 152/153, an outline dictionary's or
item's `/Count` must equal the number of *visible* items — and a closed item hides everything below
it from the outline panel, so its descendants must not contribute to any ancestor's `/Count` above
it. Because `Add` only looked at the item being inserted and blindly walked to the root, a closed
item with open children still let those children's `+1`s propagate past it. The same accumulation
also never wrote `/Count` at all for an item whose descendants were *all* closed (`OpenCount` stayed
0), even though the entry is required whenever the item has any descendants, open or not.

The fix drops the incremental accumulation entirely and computes `/Count` from the actual tree shape
once per save. `PdfOutline.ComputeVisibleDescendantCount` walks bottom-up from the root (the only
place `PrepareForSave` is ever entered — `PdfCatalog.PrepareForSave` → root `PdfOutline`): for each
item, its `_visibleDescendantCount` is the number of immediate children (each always counts, since a
child is visible whenever its parent is open) plus, for each child that is itself `Opened`, that
child's own `_visibleDescendantCount` recursively. Because the walk starts at the root and touches
every node exactly once, each item's count is available by the time its own `PrepareForSave` branch
reads it — no need to recompute per node. `/Count` on an item is then `(_opened ? 1 : -1) *
_visibleDescendantCount`, written whenever the item `HasChildren`, not only when the count is
positive.

## What was deliberately not touched

`PdfOutline.CountOpen()`/`PdfOutlineCollection.CountOpen()` are a separate, already-known-broken stub
(`CountOpen_DoesNotDescendIntoChildren` documents it) — unrelated to `/Count` written to the PDF and
left alone.

## Evidence

- New `PrepareForSave_ClosedIntermediateItem_ExcludesHiddenDescendantsFromAncestorCounts`
  (`PdfOutlineSaveTests.cs`): reproduces the issue's exact repro tree (`Top` open → `Closed mid`
  closed → two open `H3` children) and asserts root=2, `Top`=1, `Closed mid`=-2 — all three were
  wrong before the fix (root and `Top` over-counted to 3 and 2 respectively).
- New `PrepareForSave_ClosedItemWithClosedChildren_StillWritesCount`: an item with only closed
  descendants (no open items anywhere in its subtree) now still gets `/Count = -2` instead of no
  `/Count` entry at all.
- Full `net8.0` suite (8552 passed) and `dotnet build PeachPDF.slnx -t:Rebuild` (0 warnings) both
  green after the change. Diff coverage confirmed via `coverage.cobertura.xml` — every changed line
  in `PdfOutline.cs`/`PdfOutlineCollection.cs` hit.
- Found while implementing #711/#716 (PDF bookmark/outline generation), which was the first thing in
  PeachPDF to actually populate `document.Outlines` and made this pre-existing `PdfSharpCore` bug
  observable; #716 has landed on `main` but not yet been released (post-`v0.9.9`), so this is folded
  in as a fix rather than a migration note against a shipped release.
