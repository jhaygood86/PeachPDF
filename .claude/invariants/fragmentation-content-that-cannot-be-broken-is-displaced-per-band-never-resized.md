# Content that cannot be broken is displaced per band, never resized

_From [#509](https://github.com/jhaygood86/PeachPDF/issues/509)._

A run taller than a fragmentainer is drawn **once**, from one `CssBoxProperties.Location`, and each
fragmentainer shows the strip of it that falls in that band — the local rect is the document rect minus
`FragmentainerFragment.LocalOriginY`, clipped to the band. That is why a 620pt block, or a 1000px `<img>`,
already slices correctly across pages with no machinery at all.

**So when such a run has to resume lower on a band — below a repeated `<thead>`, per css-tables-3 §6.2 —
the answer is a per-band origin, not a taller box.** Growing the box stretches what it draws: a
`linear-gradient` sizes to its box and an `<img>` scales to its used height, so a box grown by the gaps
renders a *different* picture rather than the same one interrupted.
`FragmentEmitter.RecordFragmentDisplacement` is the seam, and it states two things because both are
needed:

- **the shift**, subtracted from the draft's `originY` so it reaches every coordinate `Localize` touches
  in one place — but **never** the membership tests, which ask where the geometry *lands* and so take
  `Displaced(rect, shift)` explicitly;
- **the band**, intersected into the fragment's `OverflowClip`. This is not tidiness. A displaced box's
  rectangle still spans its whole height, so on any band but the first it reaches up into the room the
  header is drawn in, and the strip the previous band already showed is drawn a second time underneath it
  — with **every word still claimed by exactly one fragmentainer**, so the standing census cannot see it.
  Where the run is a background or a replaced element there are no words at all and nothing sees it.

It applies to the **subtree**, not the box: the run is a row's whole content — the cell's border and tint
as much as the block inside it — and all of it moves together or the strips disagree. `BuildDraft` carries
it down the recursion rather than looking it up per box. Two exceptions, both of which cost a defect
before they were found: a `position: fixed` box's containing block is the page, so it is not in the run
even when it descends from one; and an `overflow: hidden` ancestor's clip is displaced only when that
ancestor is itself inside the run — for content in a cell it usually is, because a `<td>` clips, so this
is a conditional and not a swap.

## Every site that asks "which band" must be given the shift

A displacement has two halves — **where the box draws** and **which band claims it** — and they are
written in different places. `BuildDraft` subtracts the shift from `originY` (the first half) and passes
`Displaced(rect, shift)` to `region.Contains` (the second). But the questions defined over the *whole
box* are resolved at materialization, not in `BuildDraft`, and `LinesOf`/`RectOf`/`BandCut` are all
membership tests. With the shift unreachable there, a displaced box whose **last** strip was shorter than
the gaps accumulated above it tested as being in no band at all and lost its background and borders on
that page entirely — content drawn on neither page, which is what §4.3's slicing permission exists to
prevent. `Draft.Shift` exists so materialization can ask the same question `BuildDraft` did.

The window is narrow (`depth_last ≤ (n−2)·headerRoom + (n−1)·footerRoom`) and no fixture with a
comfortable last strip enters it — which is why a green suite and two agreeing rasterizations missed it.
**Grep for `Displaced(` before adding a membership test anywhere near this code.**

## The two things that keep the strips honest

**One place decides where a band's strip begins and ends.** `BandsSpannedBy` opens every band at
`PageTopOf(k) + RepeatedHeaderRoom` and closes it at `PageBottomOf(k) - RepeatedFooterHeight`, which is
`RoomForARowIn` read as a pair of edges rather than a length. A second reading of "what does a repeated
group cost" is how the strips come to disagree by exactly one group's height.

**Displacing is only correct where the content genuinely cannot move.** Gate it on the same quantity the
straddle correction fits a row against (`depth > RoomForARowIn(container, slot + 1)`). A row that would
fit the next band is moved there, and a table whose first row straddles is moved *whole* by the epilogue's
§4.3 mover — slicing either draws it across pages it is about to vacate. The failure is not visual: the
slice bottom recorded for a sliced band tells `CssBox.PaginatedItsOwnContentWithoutBreaking` that the
table fragmented, so **the relocation stops happening at all**. Measured as a footer-only table that
should have moved to page 2 staying at Y=500.

## And a band must always take some of the run

css-break-3 §4.3: *"it must place at least some content on each fragmentainer … in order to guarantee
progress through the content."* §6.2's two quarter caps keep the repeated groups under half a uniform band
between them, so a band that leaves nothing is unreachable that way — but per-`@page` geometry can make
one band far shorter than the page the caps were measured against. Such a band gives the run its whole
height and the groups none. A band that took nothing would not terminate, which is a worse failure than
the one it avoids.
