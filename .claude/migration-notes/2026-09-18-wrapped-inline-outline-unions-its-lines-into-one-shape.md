# A wrapped inline's outline is now one connected shape across all its lines

Previously, an inline element whose `outline` wrapped across several lines left its two line-wrap
edges open: only the first line's leading edge and the last line's trailing edge were drawn, leaving
a visible gap at every internal line break — `box-decoration-break: slice`-style geometry, even
though `box-decoration-break` does not govern `outline` at all.

The outline is now drawn as **one connected shape around all of the lines**, matching Chromium: the
lines' rectangles are unioned, and the outline traces the boundary of the region they cover, running
around its concave corners rather than closing across each wrap. This satisfies CSS Basic User
Interface 4 §5.2, which draws a fragmented outline as if its fragments were joined.

Where consecutive lines do not touch — no border or padding to make each line's box tall enough to
overlap its neighbour, or a `line-height` large enough to separate them — they remain separate
pieces of that same shape, which looks like the old per-line geometry but is produced by the same
union. Adding a `border` to a wrapped inline is the most visible trigger for the difference: the
border makes each line's box taller, the lines start overlapping, and the outline becomes a single
merged outline around the whole run.

`border` and `background` are unaffected and keep the sliced geometry: their own line-wrap edges stay
open, exactly as `box-decoration-break: slice` requires for those properties.

A page or column break is a different axis and is unaffected: a block-axis edge cut by a page or
column break still stays open on both sides of it, and the union never spans two fragmentainers.

Two limitations to be aware of. The patterned and bevelled styles — `dotted`, `dashed`, `groove`,
`ridge`, `inset`, `outset` — keep a closed ring per line, since a dash fit and a per-side bevel colour
are not defined for a merged region's concave corners. And the union covers the inline's own line
rectangles only, not the border boxes of atomic-inline descendants (an `<img>` or `inline-block`
inside the outlined inline), so an unusually tall descendant does not push the outline outward the
way it does in Chromium.

Scoped to `horizontal-tb` (and `sideways-rl`/`sideways-lr`, whose line boxes lay out the same way) —
a `vertical-rl`/`vertical-lr` wrapped inline's outline keeps its previous geometry, tracked alongside
the pre-existing gap in reserving that axis's own border/padding inset (see
`.claude/accepted-gaps/no-vertical-writing-mode-layout.md`).
