# A footnote-area reservation is honored by most, not all, of §4.3's movers

`Html/Core/Fragmentation/BlockConstraint.cs`'s `RemainingBlockSize` (and so `Straddles`, and so
`FitsInFragmentainer`'s monolithic-overflow check) reads a reservation two ways depending on which slot is
asked about. **For the slot the live pass is currently filling**, it reads the live pass's own
`FragmentainerContext.BandEndInsetOf` reservation - the fix that makes a footnote area (or a repeating
table `<tfoot>`) correctly shrink the room `break-inside: avoid` and orphans/widows judge content against.
**For a different slot** (a `BlockConstraint` built via `AtNextSlot()`/`AtSlot()`, e.g. "would this fit if
pushed to the next page"), it reads `HtmlContainerInt.FootnoteAreaHeightsBySlot` directly for that specific
slot (issue #1080's `footnote-policy` needed exactly this - "does the destination page have room" - to
decide whether forcing a call there is worth it) - correct because that dictionary already holds each
slot's own, independently-seeded footnote amount, unlike `FragmentainerContext.BandEndInsetOf`'s own
"composes forward from `fromSlot`" contract, which is only valid for a reservation that is a genuine
constant across every slot it's asked about (true of a repeating `<tfoot>`, not of a footnote area, whose
height varies per page). This cross-slot path is still narrower than a full fix: a repeating `<tfoot>`'s
own reservation is not visible for a non-live slot this way (it isn't tracked per-slot the way footnotes
are, and the live context's own reservation state doesn't record which kind is active), but a strict
improvement over reading zero unconditionally for every non-live slot regardless of kind, and never a
wrong answer - see `BlockConstraintTests.cs`'s `BandEndInset_ForADifferentSlot*` facts.

Four related gaps remain, none closed by either change:

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
