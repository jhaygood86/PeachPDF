# An inline-block reserves its declared width on the line

CSS 2.1 §10.3.9 makes an atomic inline-level box occupy its own used width on the line. `FlowBox`
just accumulated word widths, so `width` on a `display: inline-block` was inert.

The flex/grid path a few hundred lines up already does exactly this, via
`coordinates.CurrentX = b.ClientRight`.

## What running it turned up

- **`ClientRight` cannot be reused here, which is why the fix looks different from its precedent.**
  On the plain inline path the child box's geometry is never assigned at all — measured on a
  `width: 160px` inline-block, `Location.X`, `Size.Width`, `ClientRight` and `ActualRight` are all
  `0`. So the declared width has to be resolved from the CSS at the advance point, against a
  `childContentStartX` captured after every branch that can establish the child's line position
  (including the left-float branch, which re-establishes it from scratch).
- **Chrome agrees to 0.1pt** on both shapes that matter: 120pt for `width: 160px` with short
  content, 91.5pt for an empty `width: 120px` box with a 1px border. That second one is the case an
  accumulate-the-words flow gets most wrong — no content means no room at all.
- **`box-sizing: border-box` has to be taken off, and the obvious helper does the opposite.**
  `childContentStartX` is the content-box start and `rightSpacing` is added after, so what belongs
  between them is the CONTENT width. Under `border-box` the declared width already covers the
  padding and border those two spacings re-add, so they are counted twice unless subtracted here.
  `ActualBoxSizeIncludedWidth` cannot do it: it answers what a declared size does NOT include, so
  it is padding+border for content-box and ZERO for border-box — a no-op in exactly the case that
  needs adjusting, and wrong in the case that does not. Chrome puts a `border-box; width: 120px`
  box with a 1px border at exactly 90pt; without the subtraction it came out 91.5pt.
- **Percentages are deliberately excluded.** A percentage resolves against a containing block this
  line does not know. There is a fixture asserting the exclusion so it reads as a decision rather
  than an oversight.

## Evidence

`InlineBlockDeclaredWidthTests`, four fixtures: the declared width reserved (Chrome's 120pt), the
empty bordered box (Chrome's 91.5pt), the same box under `border-box` (Chrome's 90pt), a
`border-box` box with padding (Chrome's 120pt), content wider than the declared width still
overflowing, and a percentage width left alone. The first two fail against `main`. Full suite green on net8.0
(10,256), 0 new build warnings.
