# Box tracking: BoxReport and Prince.trackBoxes / getPrinceBoxes

Phase 2 of the Prince-parity plan. `BoxReport` (public, neutral: position from the top left, in points; used margin/padding/border; kind; page
number; element; pseudo-element; text/image source; parent/children) is built lazily by `BoxReportCollector` over the **internal** fragment tree and
reached through `ScriptLayout.Pages` / `GetBoxes(element)`. The Prince shim flips Y to a lower-left origin and names the kinds.

**Load-bearing ideas:**
- Nothing is computed unless asked: `ScriptLayout` takes a collector delegate and builds the tree on first read, so `Prince.trackBoxes` is only a
  gate on the JS side, not an engine mode. A box is attributed to its script element by `HtmlTag.SourceOrdinal`, resolved against
  `ScriptDocument.ElementsInOrder()` (the same ordinal the layout-read probe relies on).
- **A fragment's `Rect` is not its piece on this page.** For a box that breaks across pages `BoxFragment.Rect` covers the whole box (a 300pt
  block reported 300pt on both pages of a 200pt page). The per-page piece is the union of `Lines[i].Slice.FragmentRect`. (`LineFragment.Rect` is
  not it either.)
- Page-local fragment coordinates become sheet coordinates through `TextRunCollector.Frame`, the same mapping the text-run API uses; a fixed or
  sticky box's viewport displacement is added as it is there.
- An element's box chain has anonymous wrappers (`BOX>BOX>BOX...>BODY`); text sits in the anonymous or element box that owns the words, so a
  script looking for text walks descendants.

**Not done (documented in docs/peachbrackets-scripting.md):** LINE, COLUMN, FLEXLINE and FLOATS kinds, `baseline`, per-box `style`, and
`PDF.pages` (that belongs with the PDF object in PeachPDF, phase 4). Text comes before child boxes regardless of source order.
