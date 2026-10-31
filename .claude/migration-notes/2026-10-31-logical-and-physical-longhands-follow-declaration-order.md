# A logical and a physical longhand on the same edge: the later declaration wins

**Before:** a logical margin/padding/inset/border longhand (`margin-inline-start`, `border-block-end-color`, ...)
always overwrote the physical longhand on the same edge, whichever was declared later. With
`margin-inline-start: 10pt; margin-left: 20pt` under `direction: ltr` the left margin was 10pt.

**Now:** per CSS Logical Properties 1 §1.1 the two are one cascade, so the later declaration wins (20pt above;
reversing the order gives 10pt). This holds across rules, for a physical shorthand (`margin: 20pt`) written after
the logical longhand, and under `direction: rtl` and vertical `writing-mode`s, where the edge the logical
longhand maps to differs. A document that relied on the logical value winning regardless of order and declared
the physical longhand later will now render the physical value.
