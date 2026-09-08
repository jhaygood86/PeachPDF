# `FragmentEmitter` no longer knows `CssProxyBox` exists

`FragmentEmitter.ChildrenOf`/`BuildDraft` used to reach a repeating `<thead>`/`<tfoot>`'s content by
type-checking the box being visited (`box is CssProxyBox proxy`), unwrapping to `proxy.SourceBox`, and
excluding it from pruning eligibility everywhere via a dedicated `Owner: CssProxyBox?` field threaded
through `FragmentKey` and the pruning predicates. This tied the fragment-emission/pruning layer — the
exact code #917's real fix (still to come, in a separate change) has to touch — to a layout-engine-internal
detail of `CssLayoutEngineTable`'s repeating-header mechanism.

**The fix.** Generalized the *existing* multi-column mechanism instead of special-casing proxies.
`NestedFragmentainer` (a multi-column column's captured geometry, handed to the emitter explicitly by
`CssLayoutEngineColumns.FillColumns` rather than discovered by walking the tree) is renamed
`CapturedInstance` and gains one new field, `DetachedSourceRoot: CssBox?` — null for an ordinary
multi-column capture, non-null for a repeating group's detached source subtree.
`CssLayoutEngineTable.CreateHeaderProxy`/`CreateFooterProxy`'s eight call sites now call a new
`RecordRepeatingGroupFragmentInstance` helper right after each proxy's own `PerformLayout`, handing the
emitter `(tableBox, slot, band, proxy.SourceGeometry, proxy.SourceBox, self, parentContext)` — the same
shape multi-column already used. `ChildrenOf` no longer type-checks `CssProxyBox` at all: its
captured-instances loop yields `capture.DetachedSourceRoot` directly when set, exactly mirroring the
`Holds`-based filtering it already does for multi-column. `CssBox` gains a generic
`IsFragmentWalkPlaceholder` virtual (only `CssProxyBox` overrides it true) so the walk's own
not-held/fallback passes over `box.Boxes` skip a proxy still sitting in `tableBox.Boxes` without naming the
type. `MayBeObservedEmpty`/`ContentStaysInOneRun` dropped their `owner is null`/`is not CssProxyBox`
conjuncts — `capture is null` already covers the same ground once proxies are just another `CapturedInstance`
source. `StructureTagMapper`'s `if (box is CssProxyBox) return None` guard is now unreachable (`fragment.Box`
can never be a proxy) but left in place as defensive dead code rather than removed in the same change.

**A real regression, caught by the parity oracle, not code reasoning.** `FragmentKey`'s existing
`EnclosingContext` field (`capture?.Self`, disambiguating one multi-column column's fragments from
another's, and — one level deeper — one outer column's fill of a nested inner container from a different
outer column's fill of the same inner container) looks like the obvious replacement for the removed `Owner`
field once `Owner` is gone. It is not: `_fragmentRange`/`_fragmentsOf` (the "concatenate all of one box's
own fragments into an unbroken §6.2 strip" dictionaries) were keyed by `(Box, Owner)` on `main` — and
`Owner` was *always* null for multi-column, which is exactly what let a box split across two columns share
one key and concatenate correctly. `EnclosingContext` is deliberately *unique* per column instance (that is
its entire purpose for `_rectangles`/`_spans`), so keying `_fragmentRange`/`_fragmentsOf` by it instead
made every multi-column instance of a split box its own ungrouped entry — `UnbrokenBlockStripOf` silently
stopped concatenating anything, since `fragments.Count` was never ≥ 2 at any one key. Four tests caught this
immediately (`ABoxSplitAcrossColumns_IsMeasuredAgainstTheConcatenationOfItsFragments`, its sibling on block
edges, and two `BoxDecorationBreakPaintIntegrationTests` gradient/border cases) — each reporting a single
column's own extent (~191pt) where the concatenated total (~491pt) was expected. Confirmed pre-existing on
`main` (all four pass against the unmodified branch) before concluding the regression was this change's own.
**Fix:** added a *third*, separate `FragmentKey.RepeatingGroupInstance` field — `capture?.Self`, but only
when `capture.DetachedSourceRoot is not null`, else always null — and rekeyed `_fragmentRange`/`_fragmentsOf`
(and their three call sites: `RecordSpansAndRectangles`'s `boxKey`, `HasFragmentBeside`,
`UnbrokenBlockStripOf`) onto it instead of `EnclosingContext`. This is invariant across a box's own
multi-column instances (matching `Owner`'s old behavior exactly) while still varying per page for a
repeating group's source root (needed so two different pages' occurrences of the same source subtree are
never wrongly concatenated into one "unbroken strip" — a repeated header is repeated wholesale, never
divided, so `Continuing`/`ContinuedFrom` are always empty for it and concatenation across pages would be
nonsense regardless).

**Evidence.** Full suite with the pruning-parity oracle enabled
(`PEACHPDF_VERIFY_FRAGMENT_PRUNING=1`): 10,174 passed, 0 failed, including the four tests the regression
broke and the plan's pinned repeating-header/proxy tests
(`FragmentEmitterTests.RepeatingTableHeader_ProducesHeaderFragmentsOnEveryPageItRepeatsOn`,
`FragmentPruningParityTests.ARepeatingTableHeaderAcrossPages_PrunesWithoutChangingTheTree`,
`RepeatedTableHeaderClipIntegrationTests`, `CssProxyBoxTests`, `TableOncePerTableTests`,
`TableHeaderRepetitionIntegrationTests`). Solution-wide `dotnet build -t:Rebuild`: 0 warnings, 0 errors.
Diff coverage against `main`: 100% (326/326 lines). A 4-page repeating-header/footer document rendered
through the `peachpdf` CLI, rasterized at 100 DPI through both PDFium and MuPDF before (stashed) and after
this change: byte-for-byte identical pixel buffers on every page under both renderers.

**Not a fix for #917 itself.** This is purely the architectural decoupling `FragmentEmitter` needed before
its actual pagination-cost fix could be designed without re-introducing proxy-specific special-casing.
Repeating-group content still cannot earn a pruning mark after this change (`MayBeObservedEmpty` still
excludes anything reached via `capture is not null`) — that refinement, and the dominant fix (a
geometrically-proven mid-pass commit inside `EmitPass`), are a separate, subsequent change.
