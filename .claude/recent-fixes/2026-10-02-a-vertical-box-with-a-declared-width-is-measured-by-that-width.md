# A vertical box with a declared width contributes that width to its parent's intrinsic size

**Symptom:** in the `writing_mode` showcase, section 8's second flex item came out 23pt wider after the UAX #14 line-breaking
change (the third item moved right by the same amount), and section 9's first item was narrower than its own box, so the
second item was drawn over it and hid the "First." block. `text_overflow` section 4 had two vertical boxes about 200pt apart.
Bisected (harness, label x position) to #1405.

**Cause:** `CssBox.GetMinMaxSumWords` walks a subtree's words as one horizontal line. A `writing-mode: vertical-rl` box inside
a horizontal flex item is an orthogonal flow root whose words run down the page, but its words were summed into the line as
if horizontal, so a 15-character vertical box asked for 15 characters of width. The old line breaking treated a run of kana
as one word, which undercounted by luck; #1405 splits it, so the sum grew. Section 9's narrow item is the same defect the other
way round (an old measurement that happened to come out too small).

**Fix:** an orthogonal-flow child with a definite, non-percentage width is opaque to the walk: its content is skipped and the
declared width is folded in by the existing explicit-width step (css-sizing-3 §5.1: the max-content contribution of a box with a
definite size is that size). An orthogonal box with `width: auto` is unchanged.

**Evidence:** `OrthogonalIntrinsicWidthTests` (2 of 3 fail without it); full suite 15021 passed; of the 199 showcases only
`writing_mode` and `text_overflow` change (section 8's third item back at 305pt as in v0.9.20, section 9's boxes no longer
overlap, section 4's boxes sit side by side).
