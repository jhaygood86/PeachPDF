# A line whose ink rises above its line box is drawn when a break moves it to a page's top

**Before (v0.9.20):** when a line's font was taller than its `line-height` (a 13pt heading inheriting a
12pt `line-height` from `font: 10pt/12pt …`, say), and a page break moved that line to the top of the next
page, the line was drawn on no page. The typical shape was a card near a page foot whose heading had a
top margin: the card was split, an empty box top was left at the foot of one page, and the heading was
missing from the next page, while the rest of the card's text was drawn there. A card with
`overflow: hidden` used to hide this by moving whole to the next page; now that such a card breaks like
any other block, it showed up there too.

**Now:** the heading is drawn at the top of the next page, where the break put it.

**Why:** a line box is the unit that is placed on a page (CSS Fragmentation 3 §4.1), and glyphs taller than
the line have a negative half-leading that overflows the line box without moving it (CSS 2.1 §10.8.1).
The page a line belonged to was decided by the top of its ink, which sat a point or two above the page it
had been moved to, so the page it had left claimed it, and that page never reached the heading.

Confirmed against `v0.9.20`: `FragmentEmitter.ClaimsLine` computed the line's slot as
`container.SlotStartingAt(rect.Top)` from the box's ink rectangle; the `overflow: visible` form of the card
loses its heading on a `v0.9.20` build.
