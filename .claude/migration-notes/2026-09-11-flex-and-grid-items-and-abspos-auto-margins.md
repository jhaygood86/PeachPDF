# Flex/grid item generation, and `auto` block margins on a positioned box

_Landed 2026-09-11._

Three separate behaviour changes, grouped because a document tends to meet them together.

## An inline-level child of a flex or grid container is now its own item

**Before:** a flex or grid container whose children mixed inline-level and block-level boxes had its
inline run wrapped in an anonymous block, per CSS 2.1 §9.2.1.1 — a rule that belongs to *block*
containers. That wrapper became the item, so the child's own `width`/`height` sized nothing, and an
empty `::before`/`::after` was dropped from the container's items outright as an anonymous
whitespace-only box.

**Now:** every in-flow child is an item in its own right, whatever its `display` (css-flexbox-1 §4,
css-grid-2 §6). Only a contiguous run of child *text* is wrapped in one anonymous item. A
`content: ""` pseudo-element — the standard spacer idiom, sized purely by its own `width`/`height` —
is a real item.

A **floated** child is an item too: `float` has no effect inside these formatting contexts, so it
computes to `none` on an item. Before, such a child was dropped from the container's items outright and
never rendered. (A `float: footnote` body, which css-gcpm-3 really does take out of flow, is unaffected.)

**What a document author sees:** a spacer or decorative pseudo-element inside a flex/grid container now
occupies the space it declares; an inline element beside a block sibling is sized and aligned as itself
rather than through a wrapper; and a floated child appears, in the flex line where its source order puts
it. Charts.css's area and line charts are the visible case: each data label rides a `td::after` spacer up
to its own data point instead of collapsing onto the chart baseline.

A **table-internal** `display` (`table-row`, `table-cell`, and the rest of css-display-3 §2.6's set) as a
direct child of a flex/grid container is now blockified, as the spec requires. Before, it stayed
layout-internal and no engine ever laid it out: `<tbody style="display: flex">` holding ordinary `<tr>`s
rendered completely blank. It now lays out as a row of blocks.

## An unresolvable percentage height no longer collapses a flex/grid container

**Before:** `height: <percentage>` on a flex or grid container whose own containing block has no definite
height was read as a definite height of 0. A `column` container then placed every item at its content
origin, drawn on top of each other.

**Now:** such a height behaves as automatic (CSS Box Sizing 4 §5). The container sizes to its content and
its items stack normally. `justify-content` has no free space to distribute there, which is the correct
consequence of an indefinite main size.

## `auto` block-axis margins on an absolutely positioned box

**Before:** `margin-top: auto` / `margin-bottom: auto` (or their logical equivalents) on an absolutely
positioned box resolved to 0.

**Now:** with `top`, `height` and `bottom` all set, an `auto` block-axis margin absorbs the leftover
space per CSS 2.1 §10.6.4 — one `auto` margin takes all of it, two split it evenly. This applies to
both absolute and fixed positioning, and a content-sized containing block's final height is used.

**What a document author sees:** the usual idiom for vertically centring an absolutely positioned box
(`inset: 0` with a height and `margin: auto 0`) now centres it. A box combining an `auto` start margin
with a negative end margin is pushed past its containing block's edge, which is what puts Charts.css's
axis labels under the chart rather than across the top of it.
