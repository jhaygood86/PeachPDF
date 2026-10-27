# A table narrower than its `rtl` container sits against the right edge

**Before.** An in-flow `<table>` with no auto horizontal margins, narrower than its containing block, was
placed at the left edge even when the containing block was `direction: rtl` (a `margin: 0 auto` or single-auto
margin table was already placed correctly).

**Now.** It is anchored to the right-hand inline-start edge, less its right margin, as a block-level box is
([CSS 2.1 §10.3.3](https://www.w3.org/TR/CSS21/visudet.html#blockwidth): an over-constrained box ignores
`margin-left` in an `rtl` containing block). A table wider than the container overflows toward the left.
A full-width table, and every `ltr` document, is unchanged. A document that relied on the old left placement
inside an `rtl` container needs `margin-right: auto` on the table.

Checked at `git show v<previous-tag>:docs/html-css-support.md`: the page named this as a gap.
