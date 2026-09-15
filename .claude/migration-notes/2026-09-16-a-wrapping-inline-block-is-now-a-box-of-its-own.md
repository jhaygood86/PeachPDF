# An `inline-block` whose content wraps is now laid out as a box of its own

## What changed

An `inline-block` whose content **cannot fit on one line inside it** — because it declares a `width`
narrower than its content, or because its content is wider than the block around it — used to have
its words handed to the surrounding block's line breaker. They wrapped at *that* block's measure, so
the box's own content escaped it and its border and background were drawn as two disjoint strips, one
per line of the block it sat in. Where anything was above it, the upper strip landed across that
text.

Such a box now establishes its own formatting context, like a browser's: it breaks its lines at its
own width, its border box covers all of them, and it takes one place on the line that holds it. A
narrow `inline-block` holding a paragraph comes out as a narrow column of wrapped text.

Two consequences follow from the box now being its own:

- **What comes after it on the line starts at the box's own width**, not at the width its text would
  have taken unwrapped. Content that genuinely cannot be broken — a single long word — still
  overflows the box rather than widening it, and still does not move what follows it, which is what
  Chrome does with the same markup.
- **The box's last line's baseline is shared with the text beside it** (CSS 2.1
  [§10.8.1](https://www.w3.org/TR/CSS21/visudet.html#line-height)), so a wrapped label and the words
  next to it line up.

An `inline-block` whose content fits on one line is unaffected, and so is one holding block-level
content — that shape was already laid out this way.

## Also in this change

**An `inline-block` with `overflow: hidden` now paints its text.** Its clip and its content were
moved to different places when the line's baseline was resolved, so the text fell outside the box's
own clip and was cut away entirely, leaving an empty bordered box.

**An `inline-block` holding block-level content now sits on the baseline of its own last line**
rather than on its bottom margin edge, matching §10.8.1 for a box whose `overflow` is `visible`. A
`<span style="display:inline-block"><div>text</div></span>` beside ordinary words now lines that text
up with them instead of sitting a descender higher.
