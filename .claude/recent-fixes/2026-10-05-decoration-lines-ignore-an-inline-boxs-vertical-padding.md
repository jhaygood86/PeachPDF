# Decoration lines ignore an inline box's vertical padding and border (#1626)

An inline box's own decoration rectangle is widened at the block-start and block-end by its padding and border.
`PaintDecoration` took `underPos`/`overPos`/`throughPos` straight off that widened rectangle, then subtracted a
block-end inset from every keyword. A baseline-hung underline (`baseline + clearance`) was therefore pulled up by
the padding: `padding-bottom: 12pt` put it 12pt too high, through the glyphs; `padding: 10pt 0` 10pt; a 3pt
top and bottom border pushed it 3pt down.

Fix: under horizontal-tb the three positions are derived from the text area (the rectangle minus the box's own
block-start/block-end inset), and the per-keyword inset adjustment applies to vertical modes only. css-text-decor-3
§2.5 places the lines from the text's metrics, not from the decorating box's edge. Overline and line-through had the
same drift (overline moved up with top padding, line-through to the middle of the padded box), so they are fixed together.

Trap: the first attempt only skipped the inset for the underline; overline/line-through tests then failed, which is
why the positions themselves are re-based. `text-underline-position: under` keeps hanging from the text bottom
(covered by a test). Vertical writing modes are unchanged.

Evidence: `InlineUnderlinePaddingTests` (11 cases; fail without the fix), full net8.0 suite green apart from the
pre-existing `AListItemCostsLittleMoreThanAPlainBlock`, zero-warning rebuild.
