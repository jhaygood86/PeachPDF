# A `<tfoot>` §6.2 declines to repeat can hang past the content band

_Tracked as [#518](https://github.com/jhaygood86/PeachPDF/issues/518)._

[css-tables-3 §6.2](https://www.w3.org/TR/css-tables-3/#repeated-headers) repeats a footer only where it
carries an avoiding `break-inside` and is shorter than a quarter of the page. A `<tfoot>` that fails
either is laid out once, under the table's last row, by `CssLayoutEngineTable`'s step 5.

The room a *repeating* footer needs is reserved out of every band the table spans — at row granularity
(`availableHeight`, `RoomForARowIn`) and inside a cell (`FragmentainerContext.ReserveBandEnd`, from
[#493](https://github.com/jhaygood86/PeachPDF/issues/493)). Applying §6.2's conditions
([#494](https://github.com/jhaygood86/PeachPDF/issues/494)) gates all of those on the footer actually
repeating, through `RepeatedFooterHeight`. **It has to**: leaving them unconditional keeps charging every
band for a footer that is not drawn there, which is the entire cost the conditions exist to remove.

Nothing then guarantees the closing footer fits the band its last row ended in, and there is no
break-before-the-footer path.

## Measured

A6, 12mm margins (352pt band), a declined `<tfoot>`, sweeping the body cell's word count in fours from
120 to 200 — 21 documents, of which four consecutive put the footer past the band:

| words | footer bottom | band bottom |
|---|---|---|
| 132 | 375.7 | 385.5 |
| **133–136** | **386.7** | **385.5** |
| 137 | 46.2 (next page) | 385.5 |

Bounded by roughly one row's height, and it self-corrects the moment the last row itself moves on: the
footer hangs a hairline into the bottom margin rather than being clipped or lost. PDFium and MuPDF agree.

**It is reachable on an ordinary many-row table too, and it is platform-sensitive.** Windows CI found it
independently: `AGroupDeclinedBySixTwo_IsNotRedrawnAtABreakBetweenTwoRows`'s tall-`<tfoot>` case reported
the footer claimed by fragmentainers `[2, 3]` where Linux gave `[3]`. One `CssProxyBox`, two claiming
bands — a straddle, not a repetition. So a test that asks *which fragmentainers claim a declined footer's
word* is asking a question whose answer depends on font metrics; ask how many times the group was
**placed** (`AssertLaidOutOnce`) instead, which is the claim §6.2's conditions actually make. The
fragment-tree slot assertions are kept for the header, which is placed at a band's top and cannot
straddle for this reason.

## Why it was not fixed with #494

Both alternatives cost more than the defect. Reserving the room unconditionally is the per-band charge
#494 removes. Giving step 5 a break-before-the-footer path changes where a *repeating* footer lands too,
and that placement is load-bearing for #493 — step 5's footer closes the **table**, step 5a's closes a
**page**, and the two are deliberately different footers.

Only reachable for a group §6.2 declines, and only at the word count where the last row ends flush with
the band's foot.

Stated reader-facing under [Page Breaks](../../docs/html-css-support.md#page-breaks).
