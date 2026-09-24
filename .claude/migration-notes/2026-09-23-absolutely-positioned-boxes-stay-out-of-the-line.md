# An absolutely positioned box inside inline content no longer breaks the line

**Before.** An absolutely positioned box among inline content was treated like a block inside an
inline. The line ended at it, so the text after it started a new line
(`aaa<span style="position: absolute">x</span>bbb` rendered `bbb` on a second line). An inline
element around it was split in two, and the split halves dropped the element's `outline`. The box was
also moved out of that inline, so a `position: relative` inline was not its containing block: its
`top`/`left` were measured from the next positioned block ancestor, or the page.

**Now.** The box stays out of the line. The text around it stays on one line, the inline around it
is not split, and a positioned inline ancestor is its containing block, formed from the inline's padding
edges (CSS 2.1 §10.1). When the inline wraps, the edges come from its first and last line fragments, as
CSS Positioned Layout 3 §2.1 assigns them, still measured at the padding edges as in Chromium. An inline holding nothing but the positioned box is a zero-width box at its place in the line.
Separately, an inline that *is* split around an in-flow block now keeps its `outline` on both halves.

Two measurement changes come with it. An absolutely positioned box no longer widens a shrink-to-fit
container: `<div style="float: left">short <div style="position: absolute; white-space: nowrap">…long…</div></div>`
measured 189.6pt wide in `v0.9.19` and now measures 24.9pt, the width of `short`. And it no longer counts
toward a table cell's content when the cell's `vertical-align` is `middle` or `bottom`: a middle-aligned
cell with a `top: 100%` child put its text 4.8pt above the cell's content box in `v0.9.19`, and now
centres it on the text alone. A box with `top` or `bottom` set stays where its containing block puts
it. One with both `auto` still moves with the aligned text, as it did before, whether it is a direct child
of the cell or nested in an inline in it.

**Why.** CSS 2.1 §9.2.1.1 splits an inline only around an in-flow block-level box, and an
absolutely positioned box is out of flow (§9.6), so it contributes to neither a container's intrinsic
width nor a cell's content height (§10.6.3). Confirmed against `v0.9.19`: `DomUtils.ContainsInlinesOnly`
and `DomParser.ContainsInlinesOnlyDeep` treated an absolutely positioned child as block-level there, and
both measurements above were probed on that tag.
