# A column's recorded band is the one it filled, not the target it was given

_Landed 2026-07-27._

[Issue #406](https://github.com/jhaygood86/PeachPDF/issues/406),
[CSS Multi-column 1 §3.3](https://www.w3.org/TR/css-multicol-1/#pseudo-algorithm).

A `<table>` inside a `column-count` container was laid out in full and only partly emitted — 10 words
on the box tree, **5** in the fragment tree, the cell's first block only. No break value was needed;
the plain nesting was enough.

**The load-bearing observation is that the band a column is *given* and the band it *fills* are
different numbers, and only unbreakable content makes them differ.** `column-fill: balance` derives
the target from the content, so a two-column container holding one 54pt table gets a 27pt target; the
table is atomic to this engine, is placed whole, and reaches 74. `RecordNestedFragmentainer` was
handed `(boxTop, boxTop + target)` — so everything below 47 was inside no `FragmentRegion` at all. One
`Math.Max` with the bottom the fill actually reached, which the loop already computes for
`contentBottom`.

**Widening the block axis cannot let a neighbouring column claim the same content**, which is the
thing to check before believing this is safe: columns of one container share a band and are told apart
by the **inline** axis, and nothing outside the container is ever asked about a nested region
(`FragmentEmitter.ChildrenOf` only hands a `NestedFragmentainer` down inside the subtree that recorded
it). The "every word claimed exactly once" invariant is what would fail if that were wrong.

**It also recovered a line the showcase had been dropping**, which is the more interesting half:
`multicol` section 9's `box-decoration-break: clone` continuation panel had been rendering *one* of its
two lines, with its border closed above the missing one. Its column's fill likewise reached past its
target. Nothing in the unit suite noticed either loss.

**The grid case looked like the same bug and was not**, which is worth remembering before assuming a
nesting defect: a `display: grid` in the same position was losing content past the column's *inline*
edge. Filed as [#414](https://github.com/jhaygood86/PeachPDF/issues/414) as an engine-independence
(#166/#315) problem — and that reading was wrong. See the grid-track entry beside this one: it
reproduces with no multi-column container in sight, and the column merely exposed it. The thing that
showed this was measuring the same fixture *without* the container.

Tests: `MulticolLayoutIntegrationTests` (+2) and `SuppressedPassFragmentainerTests`' own case
**promoted from the characterization it was drafted as** (0 → 5 → all 10). Full net8.0 suite green
(6473); **100% diff coverage**. **65 of 66 showcases byte-identical**; `multicol` differs, and it
differs by the recovered line. Verified in both PDFium and MuPDF.
