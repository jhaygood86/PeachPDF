# Content after an absolute multi-column box is placed below it

_CSS 2.1 §9.3.1 (an absolutely positioned box has no impact on later siblings). Tracker:
[#1377](https://github.com/jhaygood86/PeachPDF/issues/1377)._

An absolutely positioned box is laid out unbroken (`CssBox.LayoutBlockChildUnbroken`), so its break never
ends the layout pass, and the content after it is placed where it would be without it. One that is or holds
a multi-column container cannot be: the columns engine records each column for one page slot and needs the
attached fragmentainer, and laid out unbroken it lost its last column lines (#1376 is the same cause). It keeps
the breaking path, so a break inside it ends the pass, and the next pass resumes inside it on the next page.

When such a box precedes a box with only stepped-over boxes before it, `DomUtils.GetPreviousSibling` still
returns the parent's absolutely positioned first child (`IsAPrecedingBreakingAbsoluteBox`), exactly as on
`main` before #1349: the content after it is laid out below that first child and paginated normally. The
first child need not be the multi-column box: checking only that lost the content after a plain absolute box
(`top: 80pt; height: 80pt`) followed by a multi-column one, which `main` draws on page 2.

Three attempts to put that content at its §9.3.1 position failed review:

- **At its position, nothing else**: it landed on the page the resumed pass had already emitted,
  and a paragraph after the box, or all ten paragraphs of a following block, were drawn on no page.
- **Below every such box**: a box on an earlier page (`top: 0`, inside an `overflow: hidden`
  wrapper on page 2) pulled the wrapper's content before and after it backwards, and page 2 went blank.
- **At its position, re-opening the emitted page**: a short block was drawn, but no pass
  paginates content laid out behind it. A following `columns: 2` block lost A1–A24, its whole first page,
  and a following 13-paragraph block had A13 sliced across the page margin.

A box later in its parent (after in-flow content) is stepped over as before, and on `main` too the content
after it is placed back on the emitted page and lost (`<div><p>B1</p><abs columns/><p>AFTER</p></div>`).

Fixing it needs the columns engine to run a column on across pages while laid out unbroken, or layout to
fragment the absolute box as a parallel flow (css-break-3 §2.1) independently of the in-flow content.
