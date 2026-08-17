# A footnote-area reservation is honored by most, not all, of §4.3's movers

`Html/Core/Fragmentation/BlockConstraint.cs`'s `RemainingBlockSize` (and so `Straddles`, and so
`FitsInFragmentainer`'s monolithic-overflow check) now reads the live pass's own
`FragmentainerContext.BandEndInsetOf` reservation - the fix that makes a footnote area (or a repeating
table `<tfoot>`) correctly shrink the room `break-inside: avoid` and orphans/widows judge content against.
This is deliberately **narrower** than `BandEndInsetOf`'s own "composes forward across every later slot"
contract: `BlockConstraint.BandEndInset` only answers for the exact slot the live pass is currently filling
(`Fragmentainer.SlotIndex == HtmlContainerInt.CurrentFragmentainer.SlotIndex`), reading zero for a `BlockConstraint`
built via `AtNextSlot()`/`AtSlot()` for any other slot. A repeating `<tfoot>`'s reservation is a genuine
constant across every slot a table spans, so composing it forward to a different slot is correct - but a
footnote area's reservation varies per page (`HtmlContainerInt.FootnoteAreaHeightsBySlot`), so composing
the *live* page's own footnote amount forward onto a query about a *different* page would misattribute one
page's reservation to another's. The narrower answer is a strictly safer regression from main (which had
no reservation awareness in `BlockConstraint` at all) than a wrong nonzero one would be.

Four related gaps remain, none closed in this change:

- **CSS Fragmentation §5.2's margin-truncation mover** (`BlockConstraint.EndingAt`/`FallsPast`, driven from
  `CssBox.ResolveBlockChildOffset`) asks a different question - a bottom-edge/tolerance check against the
  raw band (`HtmlContainerInt.FallsPast`), not `RemainingBlockSize` - and fixing it would mean threading the
  reservation into `PageBand`/`Fragmentainer.Band` itself, not just `BlockConstraint`.
- **The keep-with-next run pre-check** (`CssBox.cs`, the `extraAbove <= boundary.AtNextSlot().NextBandHeight`
  comparison guarding whether a preceding `break-after`/`break-before: avoid` run can be pulled onto the
  next page alongside a relocated child) still reads `NextBandHeight` directly, and even if it read
  `RemainingBlockSize` would fall under the same-slot restriction above (it asks about the *next* slot).
- **The orphans/widows whole-box push** (`CssBox.cs`, `ActualBottom - Location.Y <= constraint.NextBandHeight`)
  also still reads `NextBandHeight` directly, and (unlike the two calls above) is not obviously asking about
  the *destination* page's own band via a fresh `AtNextSlot()`-style constraint in the first place.
- **`CssLayoutEngineColumns`'s per-column `FragmentainerContext`** (a nested nested context nested inside a
  page, `inheritsSuppression: true`) is never seeded with the enclosing page's own footnote reservation the
  way `HtmlContainerInt.LayoutDocument`'s per-page loop and `LayoutTheRemainderMonolithically`'s fallback
  context both are - column content on a page that also has a footnote lays out unaware of the reserved
  strip at that page's bottom.

In practice this only matters for the narrow case of an unforced margin collapse, a keep-with-next/
widows-orphans relocation, or a multi-column container, landing exactly inside a footnote-reserved strip at
a page's very bottom - rare, and the ordinary per-word `CssRect.WouldStraddleFragmentainer` check (already
reservation-aware, unrelated to `BlockConstraint`) still correctly stops the actual *content* that would
follow in the non-column case. Revisiting these was judged higher-risk (each sits inside heavily-invariant-
documented fragmentation logic) than the value of closing this narrow gap in the same change. Filed as
[issue #756](https://github.com/jhaygood86/PeachPDF/issues/756).
