# A named-page run spanning several physical pages has a convergence loop that is bounded, not guaranteed

`HtmlContainerInt.PerformLayout`'s per-page horizontal reflow loop (`UseVariableInlineMeasure`'s own
3-iteration cap) detects a fixpoint by comparing `PageAssignmentSignature()` — each box's numeric page
index paired with that page's own active named page (`PageGeometryTable.PageBandGeometry.ActiveName`) —
pass over pass. Pairing the name in, not just the index, closes the specific blind spot issue #202
originally named: two passes that agree on which physical page a box landed on but disagree on which
named-page rule was active there (because a width change shifted where a name-transition boundary falls)
are now correctly told apart rather than being wrongly accepted as "already converged."

What this does not do is *prove* the loop converges for every document. A named page whose `@page`
left/right margin or `size` override spans **several physical pages** under one continuously-active name
has a genuine width→height→page-name feedback: the content width affects box heights, which affects which
page-number boundary falls where, which can — in principle — affect which name is active there. Proving a
bound on the number of iterations such a feedback loop needs (e.g. a `1 + T`-transition-count formula)
would need either that formula or a different algorithm entirely; neither is attempted here. The loop
instead stays at the flat 3-iteration cap this repo's `@container` convergence loop already established as
an acceptable "bounded, not guaranteed" stance (see
[container-query-convergence-loop-is-bounded-not-guaranteed.md](container-query-convergence-loop-is-bounded-not-guaranteed.md)).

No known real-world or test-suite fixture needs more than one re-pass to settle a named-page run spanning
multiple physical pages — every fixture this repo exercises, including a run deliberately constructed to
span 3+ physical pages under one active name, converges on the loop's very first iteration once the
signature fix above is in place. On cap exceeded, the last pass's result is accepted silently, the same
tradeoff `@container`'s own loop already makes. Tracked as
[#202](https://github.com/jhaygood86/PeachPDF/issues/202), narrowed to this specific remaining question —
the original issue's own within-pass named-page registration race (a box measured against its own name's
geometry *before* its own registration invalidated the stale slot) is already fixed, independently of the
signature change here, via `CssBox._measureResolvedAgainst`/`InlineSizeCameFromAnotherPagesMeasure`.
