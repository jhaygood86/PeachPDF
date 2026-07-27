# An outside `::marker` is in neither of its item's flows — and four places have to agree on that

_CSS 2.1 §12.5.1 / CSS Lists Level 3 §3.1. Tracker: [#467](https://github.com/jhaygood86/PeachPDF/issues/467)._

An *outside* `::marker` is `Boxes[0]` of every list item and is positioned beside the item's principal
block box, not inside it. It is therefore **not** an inline in the item's inline flow and **not** a
block child in its block flow, and the four places that walk those flows all ask one predicate,
`CssBox.IsOutsideMarker` — do not restate the grammar inline, and do not add a fifth walk without
asking it:

- `CssLayoutEngine.FlowBox` — the inline flow, where the marker sits for an item with inline content.
- `CssBox.LayoutBlockChildren` — the block-children loop, where it sits for an item with block-level
  content. It also steps `start` over the marker, so a resumption record naming index 0 is consumed
  by the first child the loop actually lays out rather than by one it passes over.
- `DomParser.JoinsTheInlineRun` — the parser pass that gathers an inline run into an anonymous block
  (`CorrectInlineBoxesParent`) and the `ContainsVariantBoxes` gate in front of it.
- `DomUtils.GetPreviousSibling` — see below.

**The parser must not wrap it.** Re-parented into the anonymous block built for the item's inline run,
the marker becomes a *grand*child, and both the call that positions it (`CssBox.LayoutOutsideMarker`)
and the call that paints it (`FragmentPainter.FindMarkerFragment`) scan **direct** children only.
Measured symptom: `<li><p>…</p></li>` ended layout with its marker at `(0, 0)`, claimed by no
fragment, drawn on no page — while `<li>text</li>` beside it was numbered normally.

**And the in-flow sibling walk must step over it, which is the trap.** Keeping the marker a direct
child means it is what the item's *first real child* resolves its own top against. A marker carries no
usable one: it is positioned **after** the item's children (see
[the sibling invariant](fragmentation-an-outside-marker-is-positioned-by-the-pass-that-places-its-item.md)
for why it has to be last), so its `ActualBottom` is still 0 when they ask. Measured symptom: the
`<p>` inside a block-content `<li>` was placed at document Y 0, the item laid out with **no height at
all**, and the next item drew straight over it. `GetPreviousSibling` already stepped over the boxes
that are not in the flow it describes — `display: none`, absolute, fixed, optionally a float — so the
marker belongs in that same list.

**The suite did not catch this; the showcase render did.** Every marker assertion in
`BlockContentListMarkerTests` still passed with the item collapsed to zero height, because each one
asked only about the marker's relation to its item — and that relation stayed correct while the
item's own content went to the top of the page. It took reading the rasterized `marker_styling`
showcase to see it. A marker test that does not also assert the item's content is laid out below the
item's top is not testing this.
