# `position: absolute`/`fixed` content with no positioned ancestor, inside a `<thead>`/`<tfoot>` cell, resolves against the wrong containing block

Tracking issue: [#787](https://github.com/jhaygood86/PeachPDF/issues/787).

`DomUtils.GetNearestPositionedAncestor` walks a box's `ParentBox` chain and falls back to "the root"
when it finds no positioned ancestor - correct for the true document root, but
`CssLayoutEngineTable.RemoveHeaderFooterFromTree` sets `_headerBox`/`_footerBox`'s own `ParentBox` to
`null` to detach a `<thead>`/`<tfoot>` from the live tree *before* `DetachAndMeasureRepeatedRowGroups`
lays its rows out. For any absolutely/fixed-positioned descendant inside a header/footer cell with no
positioned ancestor of its own, the walk hits `_headerBox`/`_footerBox` itself and stops there, treating
a detached, not-yet-positioned mid-tree box as the initial containing block - and since that box's own
`Location` isn't set until after its row content has already laid out, `left`/`top` resolve against
roughly `(0, 0)` instead of the real page origin. Confirmed empirically: identical
`position: absolute; top: 5px; left: 5px` content lands at the correct page-relative position outside
any table, but inside a `<thead>` cell it lands with its X coordinate alone showing a `(0, 0)` base
instead of the page's own content-area origin.

This is independent of `writing-mode`, of whether the header/footer actually repeats across pages, and
of issue #784 (found while reviewing that fix, but pre-existing and unrelated to it) - it affects any
table with a `<thead>`/`<tfoot>` containing absolutely/fixed-positioned content that has no positioned
ancestor of its own, which is a common real-world pattern (a small badge/icon overlay in a header cell).
A related, secondary gap: `BoxGeometrySnapshot.Translate`/`ReflectSubtree` (the mechanism that keeps a
repeating header/footer's painted snapshot in sync with a live-tree translation) don't have an
equivalent to `CssBox.OffsetLeft`/`OffsetTop`'s own `EscapesTranslationOf` guard, so once the primary
containing-block bug above is fixed, an out-of-flow descendant that should stay put during a group-wide
shift would still move with it in the painted snapshot - worth fixing in the same pass, since testing it
meaningfully depends on the primary fix landing first.

Not fixed here: this needs `GetNearestPositionedAncestor` (or its caller) to recognize a detached
subtree's own root and fall back to the true document root/initial containing block instead, which is a
real, separate investigation and fix - not a narrow adjustment to the #784 work that surfaced it.
