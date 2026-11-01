# Percentage padding refers to the containing block's inline size (follow-up to #2001)

CSS Box 3 refers a percentage padding to the *logical* width of the containing block. In a vertical writing mode that is
the block's height. `DerivedStyle.ContainingBlockInlineSize` returns `AvailableWidth` for a horizontal block and, for a
`vertical-rl`/`vertical-lr` one, its content height (`ResolveDefiniteHeightValue` minus borders and padding, else the height laid
out so far).

Stacked on the #2001 change, which introduced `ResolvePadding`. The mode read is the containing block's, not the padded box's:
the padding of an orthogonal child is still a share of the parent's inline size, as in a browser.

**Not done:** margins use the physical `ContainingBlock.AvailableWidth` too and are wrong the same way in a vertical mode; this
only fixes padding.

**Evidence:** two new vertical cases in `PercentagePaddingLayoutTests` (fail without the change, 20pt instead of 40pt);
engine 16955/16955, `PeachPDF.Tests` green.
