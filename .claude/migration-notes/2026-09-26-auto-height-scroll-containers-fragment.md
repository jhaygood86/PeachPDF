# An auto-height scroll container breaks across pages instead of being monolithic

**Before (v0.9.20):** every box with `overflow: hidden`, `auto` or `scroll` was monolithic. One that
straddled a page boundary moved whole to the next page. One taller than a page was sliced, and the line on
each slice boundary was lost: it was clipped on the page it started on, and missing from the next.

**Now:** a scroll container that is a block box in ordinary block flow is monolithic only when its block
size is fixed:
- a non-auto `height` with no `max-height`;
- for `overflow: auto`/`scroll`, also an `aspect-ratio`;
- in a vertical writing mode, `width`/`max-width` instead (where a `max-width` still counts).

A box capped only by `max-height` breaks too, as Chrome prints it, unless its content overflows the cap;
then it stays whole as before. The space a break leaves unused at the foot of the page counts against the
cap, so content that only just fits under it still keeps the box whole. An auto-height box breaks between its lines like any other block, as
browsers do when printing, so a tall `overflow: hidden` wrapper around paragraphs, a table or a `<pre>`
listing no longer drops a line per page.

Everything else stays monolithic as before, whatever its height:
- **What the box is:** an `inline-block`, a float (including a page float), a flex or grid item, or an
  absolutely, fixed or running positioned box.
- **What the box is inside:** a box that can't continue a break onto the next page, namely an
  `inline-block`, a float, an absolutely or fixed positioned box, a vertical writing-mode block, a
  multi-column container, a table caption or an `inline-table`.
- **`break-inside`:** anything with `break-inside: avoid`/`avoid-page`, or inside a box that has it.
- **What the box holds:** a float, an atomic inline (`inline-block`, `inline-table`, an image), or content
  laid out apart from its block flow: an absolutely or fixed positioned box, a page float, a multi-column,
  flex or grid container, or a table caption. So a clearfix wrapper around a floated menu still stays whole.

A short auto-height card near the bottom of a page, or one capped only by `max-height` whose content fits
under the cap with room to spare, is now split across the break instead of moving whole. Add `break-inside: avoid` to keep
the old result.

**Why:** css-break-3 §2 only lets a UA treat `overflow: hidden` as monolithic when its logical height is
non-auto with no max, and it only permits (never requires) the same for `auto`/`scroll`.

Confirmed against `v0.9.20`:
- `MonolithicContent.IsMonolithic` was `IsReplaced(box) || IsScrollContainer(box)`;
- `docs/html-css-support.md` listed "Scroll containers — any box whose `overflow` is not `visible` or
  `clip`".
