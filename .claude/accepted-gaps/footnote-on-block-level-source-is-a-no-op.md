# `float: footnote` on a block-level source is a no-op

`DomParser.DetachFootnoteBodies` only detects a `float: footnote` box as a footnote source when it is also
inline-level (`CssBox.IsInline` - `inline`, `inline-block`, `inline-table`, `inline-flex`, or
`inline-grid`), matching `DerivedStyle.ActualDisplay`'s own blockification exclusion for the same value.
A block-level `float: footnote` source (e.g. a bare `<div style="float: footnote">`) is left completely
untouched: never detached, never numbered, never routed anywhere - it renders as ordinary in-flow block
content, behaving as `float: none`.

The dominant real-world case for footnotes is an inline reference (`<sup>`/`<span>`) inside running text,
which this version fully supports. Supporting a block-level source too would mean replacing a block-flow
child with the synthesized inline call mid-`LayoutBlockChildren` - needing the same anonymous-block-wrapper
reasoning `CorrectInlineBoxesParent` already owns, re-run selectively for this one case - which was judged
not worth the complexity for a rare authoring pattern. Covered by
`FootnoteIntegrationTests.Footnote_OnBlockLevelSource_IsANoOp`.

Filed as [issue #753](https://github.com/jhaygood86/PeachPDF/issues/753).
