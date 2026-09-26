# A line whose ink rises above its line box is drawn when a break moves it to a page's top

**Before (v0.9.20):** a line was drawn on no page when both of these held:
- its font was taller than its `line-height`, for example a 13pt heading inheriting a 12pt `line-height`
  from `font: 10pt/12pt …`;
- a page break moved that line to the top of the next page.

The typical case was a bordered, padded card near a page foot whose heading had a top margin. The card
was split, and the heading was missing from the next page while the rest of the card's text was drawn
there.

**Now:** the heading is drawn at the top of the next page, where the break put it.

**Why:** the line box is the unit that is placed on a page (CSS Fragmentation 3 §4.1). A glyph taller than
its line has a negative half-leading, so its ink overflows the line box without moving it (CSS 2.1
§10.8.1). The page a line belonged to was decided by the top of its ink. That top sat a point or two above
the page the line had been moved to, so the page the line had left claimed it, and that page never
reached the heading.

Confirmed against `v0.9.20`: `FragmentEmitter.ClaimsLine` computed the line's slot as
`container.SlotStartingAt(rect.Top)`, using the box's ink rectangle.
