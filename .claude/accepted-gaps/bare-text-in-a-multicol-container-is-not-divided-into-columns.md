# Text placed directly in a multi-column container is not divided into columns (#1342)

**Gap:** `CssBox.LayoutContents` sends a container holding only inline content (`DomUtils.ContainsInlinesOnly`)
down `CssLayoutEngine.CreateLineBoxes`, and only one with a block-level child reaches
`CssLayoutEngineColumns`. So `<div style="columns:2">text</div>` is one ordinary inline flow at the
container's full width. css-multicol-1 §2 wants it in columns, with the inline run as an anonymous block
(CSS 2.1 §9.2.1.1).

Found while fixing [#1203](../recent-fixes/2026-09-24-multicol-floats-are-positioned-in-their-column.md):
a float beside such text used to send the container through the columns engine, which laid out the float
and dropped the text. The dispatch now only takes the columns engine for floats when nothing else is in
flow, so the text is laid out - in a single column, like bare text without a float.

**Why out of scope:** the fix is a parser change (wrap the run in an anonymous block for a multi-column
parent, alongside `DomParser.JoinsTheInlineRun`'s float and absolutely-positioned handling) that changes
how every existing multi-column document with bare text lays out, and it needs its own regression
coverage across the dispatch predicates `ContainsInlinesOnly`/`ContainsVariantBoxes`/`dispatchesToColumnsEngine`,
which #1038 showed have to agree.
