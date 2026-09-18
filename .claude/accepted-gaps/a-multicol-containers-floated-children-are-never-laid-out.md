# A multi-column container's floated children are never laid out

`CssLayoutEngineColumns.Layout` builds its own column-distribution item list with:

```csharp
var children = columnsBox.Boxes
    .Where(b => b.DerivedStyle.ActualDisplay != Keywords.None && !b.IsExcludedFromFlow
                && (b.HtmlTag != null || !b.IsSpaceOrEmpty))
    .ToList();
```

`IsExcludedFromFlow` is `IsOutOfFlow || IsRunningPositioned`, and `IsOutOfFlow` is `IsFloated ||
Position is Absolute/Fixed` — so a floated child of a `columns`/`column-count`/`column-width`
container is filtered out of `children` entirely. Unlike an absolutely/fixed-positioned descendant,
nothing lays a filtered-out float out through a separate out-of-flow pass afterward: it is simply
never reached by anything that assigns it a `Location`/`ActualBottom`/`ActualRight`, so it keeps its
default, all-zero geometry. If every one of a multicol container's children is a float,
`children.Count == 0` and `Layout` returns immediately, so the container itself gets no real height
either.

Per [CSS Multi-column Layout Module Level 1](https://www.w3.org/TR/css-multicol-1/) a float inside a
multicol container still establishes its own float context and floats within the column it falls in
— it is not supposed to disappear. This is a real, if narrow, deviation, tracked as
[#1203](https://github.com/jhaygood86/PeachPDF/issues/1203).

**Confirmed pre-existing, not introduced by [issue #1038's dispatch fix](2026-09-18-a-float-after-inline-content-shares-the-line.md).**
Widening `DomUtils.ContainsInlinesOnly` to also report `true` for a box holding only floats (so a
float shares an inline formatting context with surrounding text regardless of source order) could
have wrongly rerouted a multicol container whose direct children are *all* floats away from
`CssLayoutEngineColumns` and into the ordinary inline-flow path instead — `CssBox.LayoutContents`'s
`dispatchesToColumnsEngine` check was widened specifically to keep that from happening. Once that
was confirmed to still route correctly, the identical all-zero-geometry symptom for the exact same
repro was reproduced against unmodified `origin/main` (with the #1038 changes stashed away) byte-for-byte, proving the columns engine's own item-collection filter — not the dispatch decision this
task touched — is where the content is actually lost. Left out of scope of #1038, whose own test
coverage for this combination only needed to prove dispatch still reaches `CssLayoutEngineColumns`
at all (`MulticolLayoutIntegrationTests.FloatOnlyChildren_InMultiColumnContainer_StillSplitAcrossColumns`
pins the current, shared-with-baseline all-zero result for exactly that reason), not that the engine
positions those children correctly.
