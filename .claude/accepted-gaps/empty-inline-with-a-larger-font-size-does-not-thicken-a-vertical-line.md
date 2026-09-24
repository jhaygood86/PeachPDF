# An empty inline with a larger `font-size` does not thicken a vertical line (#1316)

**Gap:** in `writing-mode: vertical-rl`/`vertical-lr`, an empty inline element with its own larger
`font-size` does not make its column thicker. `text<span style="font-size: 40pt"></span>more` at 10pt,
`line-height: 1.2`, gives a 12pt column where the same span holding `x` gives 48pt. CSS 2.1 §10.8 sizes a
line from every inline box in it, and §9.4.2 still generates one for an empty inline element.

**Where:** `CssLayoutEngine.CreateVerticalLineBoxes` sizes a column from the words in it
(`LineBoxExtentOf`), over the flat list `MeasureAndCollectWordsInDocumentOrder` collects. An empty
inline puts nothing in that list.

**Why out of scope:** #1310 closed this for horizontal writing modes, where `FlowBox` walks the inline
boxes and can hold an empty inline's extent for the next word (`PlaceEmptyInline`,
`CssLineBoxCoordinates.PendingEmptyInlineExtent`). The vertical engine has no inline-box walk to hook
into. Fixing it means emitting a marker per empty inline into the collected list and folding it into
the next word's column the same way, which #1310 did not otherwise need.
