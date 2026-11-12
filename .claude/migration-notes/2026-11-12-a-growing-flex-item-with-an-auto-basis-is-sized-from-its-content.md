# A growing flex item with an `auto` basis is sized from its content

An item with `flex-grow` above 0 and `flex-basis: auto` (`flex: 1 1 auto`, `flex-grow: 1`, `flex: auto`) used to start from a size of 0, so a
row of them was split by the grow factors alone and the items came out equal. It now starts from its content size and takes its share of the
free space on top, as in a browser: a `flex-grow: 1` item holding a short word is narrower than one holding a paragraph. `flex: 1` and
`flex: <n>` (a `0%` basis) are unchanged, as are items with an explicit `width`, `flex-basis` or `flex: none`. A growing item is no longer
raised to its `min-width` before the free space is shared, and one wider than its `max-width` takes no share of it.

The declarative builders' `Grow()` is `flex-grow`, so it changes the same way: three `row.Item().Grow()` columns that used to be equal thirds
are now as wide as their content plus a third of what is left each. Give them the same `Width` (a percentage, say) for equal columns.

A flex item's `min-width` and `max-width` on a `box-sizing: border-box` item are now the border box, as everywhere else: before, the item's padding and
border were added to them, so `padding: 20pt; max-width: 100pt` allowed 140pt.
