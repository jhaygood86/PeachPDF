# Flex and grid items paint in one pass, and a re-laid-out inline box does not accumulate its ancestor's translation

**#1668 (reading order).** `<span style=display:inline-block>svg Shop</span><span style=display:inline-flex>svg Signup</span>` inside a flex
container extracted as `Signup Shop`. `FragmentPainter.PaintLayer` splits a stacking context's participants into a block pass and an inline pass,
and `StackingOrder.ActsAsInline` sent a block whose content is all inline to the inline pass (Acid2's `#eyes-a`). The first item (a block holding
inline content) therefore painted after the second (a flex container, whose blockified children are blocks). CSS Flexbox 1 §4.3: flex items paint
atomically, like inline blocks, in item order, so what an item holds must not decide its pass. `ActsAsInline` now excludes an item of a flex or grid
container (`IsFlexOrGridItem`), so all of them paint in the block pass in fragment order. Acid2's non-flex wrapper still defers.

**What this does not do.** `order` is not applied to painting (accepted gap, #2383): items paint in document order.

**#1440 (re-layout drift).** Still reproduced: laying a flex or grid container out again added the item's translation to the `Location` of every plain
inline box inside it on every pass (77.5pt per pass for `align-items: center`), as the issue's "likely class" said, and the vertically centered table cell
shape did too. A plain inline box's `Location` is nothing the flow writes (it places rectangles and words), so `FlowBox` now zeroes it where it already
resets the rectangles (`ResumeOrdinal == 0`, plain inline, not atomic, not out of flow). A repeated `<thead>` and multi-column shapes already passed.
Not visible in output today (paint reads rectangles).

**Evidence.** `FlexGridRelayoutAnonymousBoxTests` snapshots every box, anonymous ones included (12 of 14 fail without the change; the repeated-header
and multicol guards pass either way); `FlexGridItemPaintOrderTests`. Engine suite 19724 and PeachPDF.Tests 1931 pass. A 252-showcase SVG sweep changed
five (flex item rows): the drawing operations are identical (compared after normalizing generated ids) and the rasterized pages are pixel-identical
at 60 dpi.

**Not done.** The paint order of items with a non-default `order`.
