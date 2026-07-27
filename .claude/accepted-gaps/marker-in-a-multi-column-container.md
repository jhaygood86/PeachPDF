# An outside `::marker` goes with the column its item ends in

Inside a multi-column container, a `<li>` whose own text crosses a column boundary shows its bullet or
number beside the **continuation** in the later column rather than beside its first line in the
earlier one. [CSS 2.1 §12.5.1](https://www.w3.org/TR/CSS21/generate.html#lists) and
[CSS Lists Level 3 §3.1](https://www.w3.org/TR/css-lists-3/#marker-position) put it beside the item's
**first** line box, so this is a real deviation. Tracked as
[#468](https://github.com/jhaygood86/PeachPDF/issues/468).

**Why, and why it is not the marker's own doing.** A box that does not finish in a column is laid out
*again* at the next column's inline position (`CssBox.ResumeInTheNextFragmentainer`, gated on
`FragmentainerContext.HasOwnBand`), so by the end of the page pass its live `Location` describes only
its **last** fragment — `FragmentEmitter`'s own remarks say so. A marker is positioned against its
item's border box, so it can only be where that `Location` is. The page grid is not like this, which
is why [#444](https://github.com/jhaygood86/PeachPDF/issues/444) was fixable at all: there a box that
does not finish keeps the position the pass that placed it gave it.

**The alternative was tried and measured worse.** Positioning the marker on the pass that *starts* the
item — which is what #444 needs on the page grid — puts it in the column the item has left: a bullet
beside nothing in column 1, no bullet at all beside the item's own text in column 2, on a single page
with no page break anywhere in it. It also leaves that column's `BoxGeometrySnapshot` holding a second
copy of the marker's word origin, so the marker is claimed by two fragments. In a 728-document sweep
over `column-count: 2` (both `column-fill` values, 4–14 items, page heights 120–300pt), that shape
affected **661** documents against **12** for the shipped behaviour. Hence
`CssBox.MarkerBelongsToTheFragmentainerBeingFilled` asks `HasOwnBand` — the very predicate that
decides whether the box will be re-placed — and
`StraddlingListMarkerTests.AnItemCrossingAColumnBoundary_KeepsItsMarkerBesideItsOwnColumn` pins it.

Closing it properly needs the marker's geometry to be a per-fragment fact rather than one live
position, which is where #366/#390 are taking the rest of the model anyway.

The neighbouring limitation, for a marker that is never positioned at all, is in
[marker-on-a-list-item-with-block-level-content.md](marker-on-a-list-item-with-block-level-content.md).
