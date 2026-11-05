# Two margin bugs found by diffing article rectangles against Chrome (arstechnica.com)

A numeric comparison of every `<article>` rectangle in Chrome and in the engine (same layout width, from a throwaway engine test that fetched the page live) agreed on every x and
width and found the remaining height differences. Two came from margins.

**1. A flex or grid item's first child margin was applied twice.** The news cards' text block (`.relative` inside a `flex flex-col` container, first child `h2` with `mt-3`) came out
12px (the margin) shorter than Chrome's. `FoldOwnAdjoiningBlockStartMargins` walks the first-in-flow-child chain to build one adjoining-margin set, and did not stop at a box that
establishes an independent formatting context, so the child's margin was folded into the group that positions the flex container/item (moving it down) and again inside it, while the
item's height counted it once. The chain now stops at such a box (`DomUtils.EstablishesIndependentFormattingContext`), except for the document root's bookkeeping box, which the page's first
elements' margins are meant to reach (three root-margin tests in the suite fail without that exception). `FlexItemWithBylineTests` covers flex, column flex, grid and an `overflow: hidden` block.
The three-column cards now match Chrome's heights exactly.

**2. An atomic inline's vertical margins were ignored.** `<button class="mt-5 inline-flex">`, or any `inline-block` with `margin-top`/`margin-bottom`, got a line as tall as without the
margin and sat at the top of it (Chrome: the line holds the margin box, CSS 2.1 10.8.1). Padding already worked, so the margins now go the same way: an `inline-block`'s flowed
content is pre-shifted by `margin-top` and the line's bottom is extended by `margin-bottom` (`atomicTopInset`/`atomicBottomInset`), the declared-height branch counts both margins,
and the block-content path (`inline-grid`, `inline-table`, a wrapped inline-block) and `FlowInlineFlexChild` place the box below its top margin. Negative margins are left out (not modelled
by the line extent there). `AtomicInlineMarginTests`.

**Known limit.** An `inline-flex`/`inline-grid` box is not baseline-aligned with the text around it (it sits at the line's flow top, as before), so a line mixing text with such a box that has
a vertical margin is taller but the box and the text are not aligned the way a browser aligns them. A box alone on its line (the usual button) matches.

**Not done / measured.** `smaller`/`larger` on screen scale by 1.2 (see its migration note). Remaining measured differences on the Ars page: one "Most read" item wraps to two lines where Chrome
wraps to three (text width, not layout; not investigated), and the top ad band is 36px taller in the engine. Evidence: Engine 18,152, PeachPDF 1,615, PeachBrackets 659 pass.
