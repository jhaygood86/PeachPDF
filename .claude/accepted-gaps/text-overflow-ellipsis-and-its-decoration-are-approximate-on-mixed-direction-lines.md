# `text-overflow: ellipsis` and its text decoration are approximate on mixed-direction lines

Tracked as [#1631](https://github.com/jhaygood86/PeachPDF/issues/1631).

On a truncated line that mixes left-to-right and right-to-left runs, where the ellipsis lands and where a
text decoration ends are approximate. The cut is planned by walking a box's words in physical order
(`PlanLineTruncation`, `FragmentPainter.TextOverflow.cs`), and a decoration is clamped to the cut
anchor on the side given by the **truncating block's** direction (`EllipsisCut.IsRtl`). Single-direction
lines, including a whole RTL line in an RTL block, are exact.

**Spec rule:** css-overflow-4 `text-overflow` places the ellipsis at the line-end edge and truncates in
visual order; css-text-decor-3 §2 decorates the kept inline content. With nested opposite-direction
runs the kept text is not a contiguous prefix in physical order, which this model assumes.

**Why out of scope:** doing it right means planning the cut over the line's runs in visual order,
across sibling boxes that bidi reordering has permuted, which the paint-time truncation does not model.
The ellipsis placement had this limitation before decorations were clamped; the clamp only inherits it
(`.claude/recent-fixes/2026-10-04-text-decoration-ends-at-the-ellipsis.md` while it lasts).

User-facing note: the `text-overflow` row in `docs/html-css-support.md`. Closing the gap means deleting
this file and that sentence.
