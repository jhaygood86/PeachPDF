# An absolutely positioned box inside inline content no longer breaks the line

**Before.** An absolutely positioned box among inline content was treated like a block inside an
inline. The line ended at it, so the text after it started a new line
(`aaa<span style="position: absolute">x</span>bbb` rendered `bbb` on a second line). An inline
element around it was split in two, and the split halves dropped the element's `outline`. The box was
also moved out of that inline, so a `position: relative` inline was not its containing block: its
`top`/`left` were measured from the next positioned block ancestor, or the page.

**Now.** The box stays out of the line. The text around it stays on one line, the inline around it
is not split, and a positioned inline ancestor is its containing block, formed from the inline's first
and last line fragments (CSS 2.1 §10.1). Separately, an inline that *is* split around an in-flow block
now keeps its `outline` on both halves, and an absolutely positioned box no longer widens its
container's shrink-to-fit width.

**Why.** CSS 2.1 §9.2.1.1 splits an inline only around an in-flow block-level box, and an
absolutely positioned box is out of flow (§9.6). Confirmed against `v0.9.19`: `DomUtils.ContainsInlinesOnly`
and `DomParser.ContainsInlinesOnlyDeep` treated an absolutely positioned child as block-level there.
