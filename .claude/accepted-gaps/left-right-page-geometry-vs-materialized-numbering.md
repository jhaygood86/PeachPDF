# A `:left`/`:right`/`:first` page's own geometry can still disagree with its materialized number when the rule changes BOTH the content-box's own dimensions, or when a named page is active

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

**The dimension-unchanged case (closed by #1039/issue #148).** `PageGeometryTable.ResolveForMaterializedPage`
re-resolves a slot's margins/sheet size against its actual materialized page number instead of its grid
number whenever doing so would NOT change the slot's own `BandWidth`/`BandHeight` — the dimensions
layout already wrapped and fragmented that slot's content against. This covers the overwhelmingly
common case: a mirrored binding-gutter `:left`/`:right` pair (equal total left+right margin, only the
split differs) keeps identical content width regardless of which side wins, so correcting *which*
margin applies is safe with no relayout needed. The same correction also correctly reattributes
`:first` once a content-empty gap precedes the actual first materialized page, whenever `:first`'s own
margins/size don't change the band dimensions either. This correction runs exactly once, in
`HtmlContainerInt.LayoutMarginBoxes`, and is baked back into the fragment tree's own
`FragmentainerFragment.Geometry` before paint ever runs — see that method's own remarks.

**The single-dimension-changing case (closed by issue #1041, this fix).** A page-side rule that
itself changes exactly ONE of the slot's `BandWidth`/`BandHeight` — an asymmetric `:left`/`:right`
margin override (different total left+right margin per side, changing width only) or a `:first`/
`:left`/`:right` `size` override that changes only the sheet's width or only its height — combined
with a content-empty gap before it, is now corrected too, via one narrow, tightly-gated extra layout
pass: `PageGeometryTable.ProbeDimensionChangingCorrection` identifies a slot eligible for this (numbers
disagree, an override could apply, exactly one of `BandWidth`/`BandHeight` would change, and no named
page is active at the slot — see the next paragraph), and
`HtmlContainerInt.TryApplyDimensionChangingPageCorrection` re-runs layout exactly once more with that
slot's rule selection pinned to its materialized page number
(`PageGeometryTable.MaterializedNumberOverrides`).

That one extra pass is never trusted blindly. Before its result is kept, BOTH the content-emptiness
signature (the ordered list of `FragmentainerFragment.SlotIndex` across the fragment tree — which
slots survived, and in what order) and `HtmlContainerInt.PageAssignmentSignature` (every box's own
page/name assignment) must agree with what they were before the extra pass, AND every gated slot's own
`BandWidth`/`BandHeight` must actually equal what was predicted. A width or height change can
legitimately perturb pagination elsewhere (a narrower/taller page can push a box that barely fit onto
an extra page, or pull one back), and correcting the geometry without re-checking for that would risk
baking in content sized or clipped against dimensions its neighbors' page assignment no longer agrees
with. If ANY of that disagrees, the corrected pass is discarded entirely and layout is run a THIRD time
with the override cleared — deterministically reproducing the original (declined) result, since layout
is a pure function of its inputs and nothing else changed. There is no retry beyond that one extra
pass and no partial application: a wrong "corrected" layout is worse than the honest gap this residual
already tolerated. `DimensionChangingPageCorrectionFallbackIntegrationTests` exercises this
fallback-safety path directly, with a deterministic (font-metric-free, `aspect-ratio`-driven)
fixture constructed so the corrected pass's own content-emptiness signature disagrees with itself.

**Residual still open (deliberately, tracked as [issue #1041](https://github.com/jhaygood86/PeachPDF/issues/1041), narrowed rather than closed by this fix):**

- **Both dimensions changing at once** — a page-side override where BandWidth AND BandHeight both
  differ from the grid-numbered slot (e.g. a `:left`/`:right` pair that differs in both margin *and*
  `size`). `ProbeDimensionChangingCorrection` declines this on purpose: nothing in the current
  validation can tell "a relayout would fix the width" apart from "...and also the height" with the
  same fallback-safety guarantee a single-dimension change gets, so it stays out of scope rather than
  risk a partially-verified correction.
- **A slot under an active named page** (`PageBandGeometry.ActiveName` non-null, on either the
  grid-numbered or materialized-numbered side) — declined even when only one dimension would change.
  A named-page run already drives its own bounded-not-guaranteed convergence loop (see
  [A named-page run spanning several physical pages has a convergence loop that is bounded, not guaranteed](named-page-run-convergence-loop-is-bounded-not-guaranteed.md)),
  and stacking this issue's independent one-extra-pass mechanism on top of that one was judged
  materially riskier than either alone, and outside this issue's own repro shape (which is about
  `:first`/`:left`/`:right`, not named pages). There is no separate "flagged fragile named-page run"
  registry beyond `PageBandGeometry.ActiveName` itself — that check IS how a named-page run is told
  apart from the base grid today.

Reconciling either remaining case would need a materially different validation than "did content-emptiness
and page assignment come back unchanged" (for the first) or deliberately layering two independent
relayout/convergence mechanisms and reasoning about their interaction (for the second) — both judged not
worth the risk for how narrow they are in practice: on top of needing a multi-page content-empty gap
*and* a dimension-changing page-side override in the same document (already narrow), one also needs
that override to move both dimensions at once, or to land under a named page specifically.
