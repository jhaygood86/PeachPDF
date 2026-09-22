# Column-scoped footnote convergence is bounded, not guaranteed

Reserving room at the foot of column 1 shortens it, which can push a paragraph - and the footnote
reference it carries - into column 2. The reservation moves to column 2, which lengthens column 1
again and can pull the paragraph back: a two-cycle.

It terminates on `PerformLayout`'s existing `maxFootnotePasses` cap, and `ResolveFootnotesForThisAttempt`
is always the last thing that loop runs, so the emitted note areas always describe the geometry
`AttachFootnoteAreas` reads - nothing is left internally inconsistent. What can be left behind is a
note area overlapping column content by up to one oscillation's worth.

Why this is where the line is drawn: the per-column reservation is read **once per column, at
`FillColumns` entry**, from a map written only *between* `LayoutDocument` calls. Within a single pass
it is therefore a constant, and the balance-retry loop sees it as a fixed inset indistinguishable from
a cloned bottom edge - its own termination conditions (`attempt >= MaxFillAttempts`,
`target >= pageBudget`, the column-span boundary) are untouched. `EstimateBalancedColumnHeight` is
deliberately left footnote-unaware for the same reason: it keeps the estimate a pure function of
content, which is what stops the two loops feeding each other directly.

Proving convergence would mean reasoning about the joint fixpoint of the footnote loop and the column
balancer together. Two sibling gaps take the same bounded-not-guaranteed stance:
[container-query-convergence-loop-is-bounded-not-guaranteed.md](container-query-convergence-loop-is-bounded-not-guaranteed.md)
and [named-page-run-convergence-loop-is-bounded-not-guaranteed.md](named-page-run-convergence-loop-is-bounded-not-guaranteed.md).

Filed as [issue #1270](https://github.com/jhaygood86/PeachPDF/issues/1270).
