# Flex/grid items that are inline elements with block children (#1543)

`DomParser.CorrectBlockInsideInline` treated a flex/grid container's children as one inline run and
split `<span><h4/><p/></span>` around its block children, hoisting them up to become the items
themselves (two spans rendered as four items; in flex the heading and paragraph merged).

A flex/grid container has no inline formatting context: every in-flow child is an item and is
blockified (css-flexbox-1 §4, css-grid-2 §6), so CSS 2.2 §9.2.1.1 (which only splits an *inline box*)
does not apply at that level. The function now returns early for Flex/InlineFlex/Grid/InlineGrid and
recurses into each item as its own formatting context, mirroring the guard `CorrectInlineBoxesParent`
already had.

Test: `FlexboxIntegrationTests.InlineItemsWithBlockChildren_StayOneItemEach` (two grid layouts + flex).
