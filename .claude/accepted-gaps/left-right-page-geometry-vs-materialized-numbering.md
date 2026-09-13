# A `:left`/`:right`/`:first` page's own geometry can still disagree with its materialized number when the rule changes the content-box's own dimensions

`PageGeometryTable.Compute` resolves each pagination slot's own margins/sheet size (what a `:first`/
`:left`/`:right` `@page` rule shapes) from the raw, un-skipped grid slot index (`pageIndex + 1`),
built forward-incrementally as layout proceeds. Content-emptiness — and so the final *materialized* page
number a reader actually sees — is only known later, at `FragmentEmitter.Finish` (see
[Content-empty page slots are not materialized](content-empty-page-slots.md), a related but distinct
mechanism: that gap is about *whether* a slot is skipped at all, this one is about a *kept* slot's own
geometry disagreeing with its final page number). The paint-side margin-box **content**/page-style
selection (`PdfGenerator.cs`'s page loop, `HtmlContainerInt.LayoutMarginBoxes`) already uses the
materialized number correctly via `PageRuleResolver.SelectApplicableMarginRules`/
`SelectApplicablePageStyle` — only the earlier-resolved **geometry** could disagree with it.

**Now mostly closed.** `PageGeometryTable.ResolveForMaterializedPage` re-resolves a slot's
margins/sheet size against its actual materialized page number instead of its grid number whenever
doing so would NOT change the slot's own `BandWidth`/`BandHeight` — the dimensions layout already
wrapped and fragmented that slot's content against. This covers the overwhelmingly common case: a
mirrored binding-gutter `:left`/`:right` pair (equal total left+right margin, only the split differs)
keeps identical content width regardless of which side wins, so correcting *which* margin applies is
safe with no relayout needed. The same correction also correctly reattributes `:first` once a
content-empty gap precedes the actual first materialized page, whenever `:first`'s own margins/size
don't change the band dimensions either.

The correction runs exactly once, in `HtmlContainerInt.LayoutMarginBoxes` (despite the name, it now
walks every page unconditionally to apply this — the margin-box-content work it was originally named
for stays conditional on the document actually declaring running elements), and is baked back into
the fragment tree's own `FragmentainerFragment.Geometry` before paint ever runs. `PdfGenerator.cs`'s
page loop and `PageAnchorResolver.ResolveRectToPage` (shared by `PdfGenerator.HandleLinks`'s anchor
destinations and `BookmarkOutlineBuilder`'s PDF outline destinations) all read that already-corrected
value straight off the fragmentainer rather than re-deriving it, so the fragment tree stays the single
place this is decided — the same "paint consumes only the fragment tree" contract every other
paint-time consumer already follows (see [docs/architecture.md §6](../../docs/architecture.md)).

**Residual (genuinely unreconcilable without a relayout):** a page-side rule that itself changes the
slot's `BandWidth` or `BandHeight` — an asymmetric `:left`/`:right` margin override (different total
left+right margin per side) or any `:first`/`:left`/`:right` `size` override — combined with a
content-empty gap before it. There, content was already wrapped/fragmented against one side's
dimensions during layout; substituting the other side's geometry at paint time alone would size or
clip the printed page differently from what its content actually is, so
`ResolveForMaterializedPage` declines (returns null) and the grid-numbered geometry from layout is
kept, unchanged from before this fix. Reconciling this rump case would need either a second geometry
pass once emptiness is fully known (expensive, and risks its own convergence questions — see
[A named-page run spanning several physical pages has a convergence loop that is bounded, not guaranteed](named-page-run-convergence-loop-is-bounded-not-guaranteed.md))
or continuing to accept the gap. Narrower still in practice than before: needs both a multi-page
content-empty gap *and* a page-side override whose two sides genuinely differ in resolved width or
height in the same document.

The common case above was closed by #1039 (issue #148, now closed). The residual is tracked as
[issue #1041](https://github.com/jhaygood86/PeachPDF/issues/1041).
