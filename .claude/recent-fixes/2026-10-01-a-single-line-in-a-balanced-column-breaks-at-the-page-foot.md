# A line crossing the page's foot in a balanced column breaks instead of overflowing (#1561)

`<div style="height:185pt"></div><div style="columns:3"><p>w9_71 w9_72</p></div>` placed both words past the page's
foot, where the PDF clips them. Every spacer from 179 to 196pt did it, with a paragraph, a bare `div` or an `h3`.

**Cause.** A balanced container's first column band is an even share of its content (here 12pt / 3 = 4pt, taller
only as fills carry something over), so `CssRect.WouldStraddleFragmentainer` and `LineFitsNoFragmentainer` found a
12pt line "too tall for any column" and let it overflow. The exemption exists so a line too tall for every column is
not broken for every column there is. #1550 gave the exemption the height a fresh *page* would give, but only when the
band reached the page's foot, which a 4pt trial band never does.

**Fix.** `CssRect.CrossesThePageFootButFitsAFreshPage`: a line whose bottom is past the page's foot (less its foot
reservations) and which would fit a fresh page is a break, whatever the trial band says. It is asked by both the word
and the line version of the exemption.

**Trap: widening the exemption instead.** The first attempt marked every balanced column's `FreshPageBandHeight`. That
made a line taller than a trial band a break too, and the container deferred page after page forever: each new page
tries a 4pt band first, breaks before the line, grows the band by a factor per attempt, and hits the attempt cap with a
carry still set. A hang, found only by running it (the suite passed up to the hang). Only a line that crosses the foot
counts; a short trial band inside the page still overflows.

**What stood in the way, in the order it was found.** Seed 194 lost 4 words with this change alone. Its reduction (two floats
as wide as their columns, text after the container) was not a page-foot case at all: main hid three separate bugs by
accident (the second float moved to the next page), and this change let the float stay where it belongs. Each was fixed in
its own change, which this one now sits on: the float text drawn in two columns, the shift of a crowded line stopping at the
column's trial band, and a multi-column container holding only a float being treated as an empty box, so the text after it
sat beside the float. Seed 162 still loses one word with the stack: a two-line left float at the foot after such a container
is laid out whole, crossing the foot (the known inline-float gap), where main moved it by the same accident.

Evidence: `MulticolContentLossTests.SingleLineContainerCrossingThePageFoot_MovesToTheNextPage` (3 of the 5 cases fail
without the change); full net8.0 suite passes; 300-document corpus against main with the changes below it: 8 documents lose
fewer words, 1 doubles fewer, 1 loses more (seed 162).