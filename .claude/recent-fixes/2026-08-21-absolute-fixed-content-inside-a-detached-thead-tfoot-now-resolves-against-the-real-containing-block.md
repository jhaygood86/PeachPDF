## Absolute/fixed content inside a `<thead>`/`<tfoot>` cell now resolves against the real containing block (#787)

Closed the gap recorded in `.claude/accepted-gaps/absolute-fixed-content-inside-a-thead-tfoot-resolves-against-the-wrong-containing-block.md`:
`DomUtils.GetNearestPositionedAncestor`'s only "is this the root" test was "`ParentBox` is null" - which is
correct for the true document root, but `CssLayoutEngineTable.RemoveHeaderFooterFromTree` also nulls a
detached `<thead>`/`<tfoot>`'s own `ParentBox` before laying its rows out. An absolutely/fixed-positioned
descendant with no positioned ancestor of its own stopped the walk at that detached, pre-layout box
instead - landing near `(0, 0)` instead of the real page origin (or a real positioned ancestor further up
the tree).

**Load-bearing idea**: add a new `CssBox.DomParentBox`, consulted only as a fallback when `ParentBox` is
null (`ParentBox ?? DomParentBox`), set to the table box wherever `RemoveHeaderFooterFromTree` nulls
`ParentBox`. This is deliberately a fallback, not a replacement - `RunningElementLayout.LayoutRunningElementFor`
(footnotes, `position: running()`) also nulls-then-reparents a box's `ParentBox`, but reparents it to a
*real* synthetic containing block sized to the margin-box/footnote-area rect, and that intentional
reparenting must keep winning: since `ParentBox` is never null while it's in effect, the walk never
reaches `DomParentBox` for that case. `DomParentBox` is only ever set in `RemoveHeaderFooterFromTree` -
nowhere else - so this can't accidentally engage for any other detached-box scenario.

**Found only by tracing the code, not by reading it**: fixing the ancestor-resolution bug alone reopened a
second, previously-latent gap. `BoxGeometrySnapshot.Translate`/`ReflectSubtree` (the mechanism keeping a
repeating header/footer's *painted* snapshot in sync with the live tree's translations, e.g. issue #784's
`vertical-rl` row-order reversal) had no equivalent to `CssBox.OffsetTop`'s own `EscapesTranslationOf`
guard - so once an out-of-flow descendant's containing block correctly resolves to something *outside*
the header/footer subtree, that descendant's entry in the snapshot would still receive the same per-row
residual shift applied to reverse the group's own internal row order, even though the live box now
correctly stays put. A new regression test (`VerticalRlTable_MultiRowThead_AbsoluteContentWithNoPositionedAncestor_SnapshotIsNotShiftedByRowAxisReflection`)
confirmed this concretely: without the fix, the snapshot's copy of the descendant's position was off by
the full 40pt row-axis residual. Fixed by widening `CssBox.EscapesTranslationOf` to `internal` and giving
`BoxGeometrySnapshot` a real recursive walk (mirroring `CssBox.OffsetTop`'s shape) that stops descending
the instant it finds an escaping box - a flat per-box check over `_geometry.Values` is not equivalent,
since it would let an in-flow descendant *inside* a correctly-skipped escaping box get shifted
independently, corrupting their relative geometry.

**Deliberately not done**: a `<tfoot>` whose only content is out-of-flow (e.g. absolutely-positioned) was
found, while writing this fix's own tests, to never get a footer proxy created for it at all - a separate,
pre-existing table-structure quirk unrelated to the containing-block bug this fix targets, recorded as its
own accepted gap ([`.claude/accepted-gaps/tfoot-with-only-out-of-flow-content-never-gets-a-footer-proxy.md`](../accepted-gaps/tfoot-with-only-out-of-flow-content-never-gets-a-footer-proxy.md),
tracked as [#791](https://github.com/jhaygood86/PeachPDF/issues/791)) - test fixtures for the `<tfoot>`
case here give the cell some ordinary in-flow content alongside the out-of-flow box to route around it.

A post-change review pass also caught, and this fixed before landing: `BoxGeometrySnapshot.ReflectSubtree`
duplicated `Translate`'s new recursive-walk logic verbatim with `dy` fixed at `0` instead of calling it
directly (now `TranslateSubtree(root, dx, dy: 0, translationRoot: root)`); a stale `CssBox.DomParentBox`
was left on a reattached `<thead>`/`<tfoot>` after a second layout pass (`RestoreStructureFromAnyPreviousRun`
now clears it back to `null` once `ParentBox` is real again, since a non-null `DomParentBox` has no defined
meaning once the box it names is genuinely back in the live tree); and the repeated `ParentBox ??
DomParentBox` fallback expression was factored into one `CssBox.EffectiveParentBox` property both
`DomUtils` methods now share.

**Evidence**: new regression tests in `AbsolutePositioningIntegrationTests.cs` (`<thead>`/`<tfoot>` cases,
a percentage-width case confirming `CssLayoutEngine.PercentageBase` - a second, independent
`GetNearestPositionedAncestor` consumer - benefits automatically, and a case with a real
`position: relative` ancestor several levels above the table, confirming the walk neither stops early at
the header nor skips past a real ancestor to the document root), `TableWritingModeIntegrationTests.cs`
(the `BoxGeometrySnapshot` guard, confirmed meaningful by reverting just that change and watching the new
test fail with the expected 40pt residual), and a new `BoxGeometrySnapshotTests.cs` (direct unit coverage
for `Translate`'s multi-root fallback branch, unreachable through any end-to-end layout today); full
`dotnet test --framework net8.0` suite green (9052/9061, 9 pre-existing platform-specific skips); 100% diff
coverage; zero `dotnet build PeachPDF.slnx -t:Rebuild` warnings.
