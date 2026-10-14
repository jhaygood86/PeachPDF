# Text beside a float in a multi-column container is not divided into columns (#1342)

**Gap:** a multi-column container whose only in-flow content is text, with a float beside that text, takes
the inline branch (`CssBox.LayoutContents` → `CssLayoutEngine.CreateLineBoxes`) and never reaches
`CssLayoutEngineColumns`. So `<div style="columns:2"><div style="float:left">…</div>text</div>` is one
inline flow at the container's full width, and the float shortens its lines without affecting the columns.
Text with no float beside it is columnized: `DomParser.CorrectInlineBoxesParent` wraps the run in an
anonymous block for that case (css-multicol-1 §3, CSS 2.1 §9.2.1.1).

Found while fixing [#1203](../recent-fixes/2026-09-24-multicol-floats-are-positioned-in-their-column.md): the
float disjunct in `CssBox`'s `dispatchesToColumnsEngine` sends a container to the columns engine only when
nothing else is in flow, so a float beside text leaves the text in the inline flow it always took.

**Why out of scope:** the wrapper is deliberately not applied to a run holding a float. Extending it to a run
beside a float changes the dispatch predicates `ContainsInlinesOnly`/`ContainsVariantBoxes`/`dispatchesToColumnsEngine`,
which #1038 showed have to agree, and needs its own regression coverage across all three.
