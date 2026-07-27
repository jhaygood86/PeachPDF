# Either side of a flex or grid break point may force it

_Landed 2026-07-27. [Issue #441](https://github.com/jhaygood86/PeachPDF/issues/441)._

[CSS Fragmentation Level 3 §3.1](https://www.w3.org/TR/css-break-3/#break-between): a forced break occurs
at a class-A break point if **either** the earlier sibling's `break-after` **or** the later sibling's
`break-before` has a forced value. The pass that decides whether a flex line or grid row moves to the next
fragmentainer read only `break-before`, so `break-after: page` on an item was silently dropped while the
same intent spelt on the following line's items took effect.

**The load-bearing idea is that the question belongs to the break point, not to either line**, which is
what makes it one predicate rather than two reads. `LineRelocation.ForcedBreakBetween(above, below)` states
it once — the same shape `BreakValues.RequiredSide` already has for block flow — and both engines call it
with the line above the break point in hand, which the walk already has because it goes in block-axis order.

**The fix landed in two places, not the one the issue names.** #441's text refers to a consolidated
`CssBox.RelocateEngineLines`; that consolidation belongs to [#390](https://github.com/jhaygood86/PeachPDF/issues/390)
and is not on `main`, so `CssLayoutEngineFlex.RelocateLinesAcrossFragmentainers` and
`CssLayoutEngineGrid.RelocateRowsAcrossFragmentainers` are still separate loops. Putting the predicate in
`LineRelocation` — where `DeltaFor` already lives, for exactly this reason — is what keeps the two copies
from answering it differently, and leaves one call site each for the consolidation to absorb.

**Grid keys the earlier side by where an item *ends*, and that is not a refinement.** Rows are grouped by
`RowStart`, so an item spanning rows 1–2 is in row 1's group — and reading a `break-after` there takes the
break at the boundary before row 2, which runs through the **middle of the item that declared it**. The
item is the earlier sibling of the row after the last row it covers, so `BoxesByEndRow` groups by
`RowStart + RowSpan - 1`, and what the loop wants is the nearest end *below* the row being asked about
rather than the previous group's key: an item spanning rows 0–1 with a plain item at row 2 leaves row 1
with no group of its own, and "the previous group" would then read the spanning item at a boundary inside
it again. Both the rows and the end rows ascend, so a cursor walks them together in one pass — the first
version scanned every placement per row, which is quadratic in a large grid and paid whether or not
anything moves.

**A `break-after` on the last line is deliberately not acted on**, and it is not simply that there is
nothing below it to move: it names the break point after the *container*, and §3.1's propagation stops
before a box whose children an engine places — a break travelling out of an item would name a position the
engine is about to overwrite. Both halves are pinned, since the naive fix (treat it as a break the
container takes) passes every other test here.

Tests: `FlexGridFragmentationIntegrationTests` (+7 — the break-after theory per engine in both spellings,
the last-line no-op per engine, and the row-spanning case). Verified load-bearing by neutralizing the
`break-after` read: **5 fail** (both engines' theories and the span case), the two last-line guards pass
either way as guards should. Neutralizing only the span-awareness (`RowStart + RowSpan - 1` → `RowStart`)
fails exactly the span case. Full net8.0 suite green (6647), CLI green (96), 0 warnings; **100% diff
coverage**.

**68 of 69 showcases are byte-identical** — the expected result, since none declared `break-after` on a
flex or grid item. `paged_media_monolithic_content` gained a section that does, and **on the unfixed build
its second line sits directly under the first on the same page** instead of opening the next one. Verified
in both PDFium and MuPDF.

**Two pre-existing deviations the review turned up, both in `DeltaFor` and both filed rather than fixed:**
a directional value forces the break but not its *side* — there is no parity walk and no blank-slot
reservation, so the line opens the next page whichever side it is
([#450](https://github.com/jhaygood86/PeachPDF/issues/450)); and a line whose top is already flush on a
fragmentainer boundary is pushed a whole further page, because `SlotStartingAt` resolves a flush top to the
later slot ([#451](https://github.com/jhaygood86/PeachPDF/issues/451), measured at exactly 160pt of filler
on a 160pt band). Both predate `break-after` being read; see
[the accepted-gap note](../accepted-gaps/flex-grid-forced-break-side-and-flush-boundary.md).

**A fixture trap worth not repeating:** `.card` is `content-box` with 12px of horizontal padding and a
border, so two items at `width: 44%` do not share a line in that showcase — they wrapped into a line each,
and the demo then read as a chain of two breaks with a near-empty page between them. The sibling section
beside it already uses `38%` for the same reason.
