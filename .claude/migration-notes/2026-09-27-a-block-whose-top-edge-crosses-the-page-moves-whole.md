# A block whose top padding or border crosses the page foot moves whole

**Before (v0.9.20):** a block whose top padding or border reached past the foot of a page stayed on that
page. Its first child was placed on the next page, but the child's first line of text was drawn at the next
page's top: above the child's own box, so the child's background and border sat lower than its text. With
a top border, the border bar could be drawn partly in the page margin and a highlighted heading inside the
block lost its background.

**Now:** the block moves whole to the next page, and its first child's text starts inside the child's box.

**Why:** a break that falls inside the block's leading edge falls before its first child, so the child
cannot be laid out on the page being left (css-break-3 §5.2 and §4.4).

Confirmed against `v0.9.20`: `CssBox.ResolveBlockChildOffset` judged a first child only against the band its
parent's content top ends in (`BlockConstraint.EndingAt`), with no check against the fragmentainer being
filled.
