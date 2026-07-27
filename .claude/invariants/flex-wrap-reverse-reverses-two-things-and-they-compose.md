# `wrap-reverse` reverses two different things, and applying either one twice cancels it

_CSS Flexbox 1 §5.3 / §8.3. Discovered closing [#459](https://github.com/jhaygood86/PeachPDF/issues/459)._

`flex-wrap: wrap-reverse` swaps the flex container's cross-start and cross-end **directions**. That
one statement lands in two different methods of `CssLayoutEngineFlex`, and they are not the same
reversal:

- `DistributeCrossSpace` reverses the **stack of lines** — each line's placed strip is reflected
  about the container's cross axis (`CrossOffset = crossExtent - (CrossOffset + CrossSize)`), so the
  first line in flow is the last one down the page.
- `ComputeCrossOffsets` reverses **where an item sits inside its own line** — `align-items`/
  `align-self`'s two flush arms exchange places, so `flex-start` names the bottom of a row line and
  `flex-end` its top, and a baseline-sharing group is flushed to the bottom.

They compose: an item ends up mirrored within a line that is itself in a mirrored stack, which is
the single swap the spec describes. **Applying either one a second time undoes it**, and the two
failures look nothing alike — a second stack reversal restores source order down the page (and
`LineRelocation.Walk` then reads the container the wrong way round), while a second within-line
reversal silently puts short items back on the unswapped edge with every line still in the right
place, which is the shape this repo already shipped for months.

Three sub-rules that a future change here can plausibly get wrong, each measured:

- **`center` must not be swapped.** Centring a margin box is its own mirror image even with unequal
  cross margins: `free/2 + marginBefore` from the top and `free/2 + marginAfter` from the bottom name
  the same coordinate. Swapping it is a no-op at best and, written the obvious way, an off-by-the-
  margin-difference at worst.
- **A stretched item's offset must not be re-derived from its measured size.** An item that fills its
  line is on both edges at once, so `line.CrossSize - itemCross - marginAfter` and `marginBefore` are
  the same number — except that the stretch re-layout writes the target size through a string
  (`FormatLayoutUnits`) and reads it back, so the two differ by ~1e-4. That is enough to redraw a
  card's whole rounded border and show up as a showcase diff: `paged_media_monolithic_content`
  reported a changed page for a 0.00006pt shift. Below the same 0.5 tolerance the stretch branch
  itself uses, take the margin.
- **The cross size read after the stretch branch is not the one read before it.** That branch can
  re-lay the item out, so an offset computed from the size captured at the top of the loop places a
  stretched item short of its line by the amount it grew.
