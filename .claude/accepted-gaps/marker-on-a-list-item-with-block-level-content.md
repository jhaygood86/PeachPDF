# A list item whose content is block-level gets no `::marker` at all

`<li><p>text</p></li>` paints no bullet or number — not in the wrong place, not on the wrong page:
the marker box ends layout at `(0, 0)`, is claimed by no fragment, and is never drawn. `<li>text</li>`
beside it is numbered normally. Tracked as
[#467](https://github.com/jhaygood86/PeachPDF/issues/467).

**Why.** An outside `::marker` is positioned by exactly one call, which scans the list item's
**direct** children for `IsMarkerPseudoElement` (`CssBox.LayoutOutsideMarker`; before #444's fix the
same scan sat in `CssBox.PerformLayoutEpilogue`). When an item's content is block-level,
`DomParser.CorrectTextBoxes` wraps its inline children — the marker among them — in an anonymous
block, so the marker becomes a *grand*child and the scan misses it. `CssBoxMarker.PerformLayoutImp`
is the only writer of a marker's geometry, so nothing ever runs.

This is a genuine deviation from
[CSS 2.1 §12.5.1](https://www.w3.org/TR/CSS21/generate.html#lists) and
[CSS Lists Level 3 §3](https://www.w3.org/TR/css-lists-3/#list-item), which place the marker beside
the item's principal block box whether its content is inline or block-level.

**Out of scope for #444**, and measured to be pre-existing rather than caused by it: the same fixture
draws only the second item's `2.` on both builds. #444 changed *when* the marker is positioned, not
*which* box is looked at. Closing this one means either seeing through the anonymous wrapper or not
re-parenting the marker into it — the latter is the smaller statement but touches `CorrectTextBoxes`,
which several other pseudo-elements go through.

The neighbouring limitation, for a marker that *is* found, is in
[marker-in-a-multi-column-container.md](marker-in-a-multi-column-container.md).
