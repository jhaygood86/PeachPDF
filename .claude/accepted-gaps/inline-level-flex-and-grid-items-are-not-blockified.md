# An inline-level flex/grid item's computed `display` is not blockified

[CSS Display 3 §2.7](https://www.w3.org/TR/css-display-3/#blockify), as required by
[css-flexbox-1 §4](https://www.w3.org/TR/css-flexbox-1/#flex-items) ("the `display` value of a flex item
is blockified") and css-grid-2 §6, blockifies **every** in-flow child of a flex or grid container.
`DomParser.NormalizeFlexOrGridItem` only does so for the **layout-internal** set (css-display-3 §2.6's
table-internal displays → `block`). An inline-level item — `display: inline`, `inline-block`,
`inline-flex`, `inline-table` — keeps its computed value.

**Why it was left.** Doing the full blockification regresses replaced elements, and it was tried before
being rejected. A replaced box takes its size from the phantom word carrying its content, and that size
only reaches the box through inline flow; as a block-level box it goes through
`CssLayoutEngine.GetBoxWidth` instead, whose `width: auto` fills the containing block rather than using
the element's intrinsic width. CSS 2.1 §10.3.4's intrinsic width for block-level replaced content is not
implemented here, so an `<img>` or inline `<svg>` flex item lost its intrinsic size outright. Five tests
caught it, `FlexboxIntegrationTests.Column_AlignItemsCenter_ReplacedItem_Centers` most clearly.

**Why it is mostly invisible.** The flex and grid engines already lay every item out blockified
(`CssLayoutEngineFlex.PerformLayoutBlockified`), so an inline item's own `width`/`height` do apply and it
is measured, aligned and painted as a block. What genuinely broke before this area was touched was the
*box tree* — CSS 2.1 §9.2.1.1's anonymous block wrapper being generated inside a flex container — and
`DomParser.CorrectInlineBoxesParent` owns that now. The remaining deviation is the computed value itself,
which is observable mainly through a future `@supports`/`getComputedStyle`-shaped surface rather than
through layout.

**What closing it needs:** CSS 2.1 §10.3.4/§10.6.6 intrinsic sizing for block-level replaced content
first. Blockifying without that trades a spec deviation nobody can see for one that breaks every image in
a flex container.

Filed as issue **#TODO** — replace with the tracking issue number once it is opened.
