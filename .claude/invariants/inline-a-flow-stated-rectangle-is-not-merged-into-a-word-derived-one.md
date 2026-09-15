# A rectangle the flow states for an inline box is not merged into one its words derive

An inline box's per-line rectangle (`CssLineBox.Rectangles`) comes from two very different places:

- **`BubbleRectangles`**, at line finalization, deriving it from where the box's words actually
  landed — all four edges;
- **the flow itself** — `CssLayoutEngine.ReserveAtomicInlineUsedWidth`, and the plain-inline branch
  beside it in `FinalizeFlowBoxExit` — stating it for a box that has no words to derive one from: an
  empty `inline-block`, the checkbox-glyph shape.

**Only one of these may state any given box's rectangle.** They are not two halves of one rectangle
to be unioned, because the flow does not know the block axis: at `FinalizeFlowBoxExit` the cursor
carries the line's flow top, `ApplyVerticalAlignment` has not run, and the box's content has not been
baseline-shifted into its final position yet.

`Rectangles` is merged min/max on every edge (`UpdateRectangle`), so a flow-stated rectangle merged
into a word-derived one wins wherever it reaches further — including upward. Measured: an
`inline-block` holding one line of 12pt text had its rectangle's top move from the ink at
`Y = 20.1845` up to the line's flow top at `Y = 20`, and its height grow from `14.63` to `14.815`.
Small, silent, and applied to every such box in the document — its background and border start half a
leading too high.

The plain-inline branch in `FinalizeFlowBoxExit` states one *unconditionally* whenever the box's
content came out narrower than its `Size.Width`, so it is safe only for as long as nothing gives a
word-bearing inline box a `Size.Width` — true today, since `width` does not apply to a non-replaced
`display: inline`. An atomic inline does have one, which is exactly why it is routed to
`ReserveAtomicInlineUsedWidth` instead of being let into that branch.

The way to state an inline-axis fact about a box whose words *do* produce a rectangle is to correct
that rectangle after `BubbleRectangles` has produced it, touching only `X`/`Width` — which is what
`WidenAtomicInlineRectangles` does for CSS 2.1 §10.3.9's declared width. Doing it there is also what
makes `text-align` come out right without any extra work: `ApplyHorizontalAlignment` has already
shifted the rectangle by then, so anchoring on its own `X` inherits the shift.
