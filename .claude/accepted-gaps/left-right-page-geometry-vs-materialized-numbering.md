# A `:left`/`:right` page's own geometry can be numbered differently than its materialized content

`PageGeometryTable.Compute` resolves each pagination slot's own margins/sheet size (what a `:left`/
`:right` gutter-margin `@page` rule shapes) from the raw, un-skipped grid slot index (`pageIndex + 1`),
built forward-incrementally as layout proceeds. Content-emptiness — and so the final *materialized* page
number a reader actually sees — is only known later, at `FragmentEmitter.Finish` (see
[Content-empty page slots are not materialized](content-empty-page-slots.md), a related but distinct
mechanism: that gap is about *whether* a slot is skipped at all, this one is about a *kept* slot's own
geometry disagreeing with its final page number). The paint-side margin-box **content**/page-style
selection (`PdfGenerator.cs`'s page loop, `HtmlContainerInt.LayoutMarginBoxes`) already uses the
materialized number correctly via `PageRuleResolver.SelectApplicableMarginRules`/
`SelectApplicablePageStyle` — only the earlier-resolved **geometry** can disagree with it. So once a
content-empty page has been skipped somewhere earlier in the document, a later kept page can render
`:right` margin-box content (correctly materialized-numbered) inside margins that were actually sized by
the `:left` rule (grid-numbered), or vice versa.

Reconciling this would need either a second geometry pass once emptiness is fully known (expensive, and
risks its own convergence questions — see
[A named-page run spanning several physical pages has a convergence loop that is bounded, not guaranteed](named-page-run-convergence-loop-is-bounded-not-guaranteed.md))
or continuing to accept the gap. Narrow in practice: needs both a multi-page content-empty gap *and* a
parity-dependent (`:left`/`:right`) margin override in the same document. Tracked as
[issue #148](https://github.com/jhaygood86/PeachPDF/issues/148).
