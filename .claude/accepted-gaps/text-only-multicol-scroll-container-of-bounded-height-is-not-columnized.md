# A text-only multi-column scroll container of bounded height is not laid out in columns (#1849)

**Gap:** `DomParser.CorrectInlineBoxesParent` does not wrap the text of a multi-column container that clips its content (`overflow` other than `visible`) and has a `height` or `max-height` (`IsHeightBoundedScrollContainer`). The text keeps the single inline flow it had on `main`, across the container's full width.

**Why:** the columns engine no longer skips words in a clipping container of definite height (it fills columns of that height and clips the rest, #1818; see [the recent fix](../recent-fixes/2026-10-22-multicol-height-cap-and-words-above-the-band.md)). What stops the wrapper is a different pin: wrapped, a scroll container taller than a page is no longer laid out whole (`MonolithicContent` exempts a box that dispatches to the columns engine) but fragmented across pages, and `ScrollContainerEstablishingColumnsButHoldingOnlyInlineContent_IsStillSuppressed` pins the one-pass behaviour. A fix has to decide whether a text-only scroll container taller than the page stays monolithic when columnized, as the same container holding block children does not.

**Not claimed:** that the wrapper and a hand-written `<div>` give the same words in every fixed-height scroll container. They do not, which is why this case is left out of the wrapper.
