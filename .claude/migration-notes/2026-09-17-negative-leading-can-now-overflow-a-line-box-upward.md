# Negative leading can now overflow a line box upward too

Previously, when a `line-height` was shorter than the used font's own height — making the leading
negative — PeachPDF floored the resulting half-leading at zero: the line's content area could overflow
its line box *downward* only, and the topmost ink always started flush with the line box's own top edge.
`line-height: 1` is already negative leading for most fonts (a typical sans-serif's own content area is
roughly 1.15em), so this was a common configuration, not an exotic one.

Fragmentainer (page/column) membership is now decided per line box rather than per word, so a word's ink
escaping above its line box can no longer escape the fragmentainer the line was placed in. With that risk
closed, the floor is gone: a `line-height` shorter than the font now lets the content area overflow the
line box on **both** sides, per [CSS 2.1 §10.8.1](https://www.w3.org/TR/CSS21/visudet.html#leading) —
matching browsers. The line's own height is unaffected either way; only where its ink sits within that
height changes. A document that relied on the old downward-only clipping (for example, tightly
overlapping lines of a very negative `line-height`) may now render its earlier lines' ink shifted slightly
upward relative to before.
