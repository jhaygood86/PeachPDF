# An auto-height scroll container breaks across pages instead of being monolithic

**Before (v0.9.19):** every box with `overflow: hidden`, `auto` or `scroll` was monolithic. One that
straddled a page boundary moved whole to the next page. One taller than a page was sliced, and the line
on each slice boundary was lost (clipped on the page it started on, missing from the next).

**Now:** a scroll container that is a block box in ordinary block flow is monolithic only when its block
size is fixed. That means a non-auto `height`, a `max-height`, an `aspect-ratio`, or both `top` and
`bottom` on an absolutely positioned box; in a vertical writing mode, `width`/`max-width`. An auto-height
one breaks between its lines like any other block, as browsers do when printing, so a tall
clearfix-style `overflow: hidden` wrapper no longer drops a line per page.

Everything else stays monolithic as before, whatever its height:
- an `inline-block`, a float (including a page float), or a flex or grid item;
- anything inside a box that can't continue a break onto the next page: an `inline-block`, a float, a
  vertical writing-mode block, a multi-column container, a table caption or an `inline-table`;
- a wrapper holding content that is laid out apart from its block flow: an absolutely or fixed
  positioned box, a page float, a multi-column, flex or grid container, or a table caption.

A clearfix wrapper around floats does break: a floated menu beside a long column of text no longer loses
a line of that text at every page boundary. The float itself is laid out in one piece, so a float that
crosses a boundary is shown as one slice per page.

A short auto-height card near the bottom of a page is now split across the break instead of moving whole.
Add `break-inside: avoid` to keep the old result.

**Why:** css-break-3 §2 only lets a UA treat `overflow: hidden` as monolithic when its logical height is
non-auto with no max, and it only permits (never requires) the same for `auto`/`scroll`.

Confirmed against `v0.9.19`: `MonolithicContent.IsMonolithic` was `IsReplaced(box) || IsScrollContainer(box)`,
and `docs/html-css-support.md` listed "Scroll containers — any box whose `overflow` is not `visible` or `clip`".
