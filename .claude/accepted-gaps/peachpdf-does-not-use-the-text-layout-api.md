# PeachPDF does not lay its text out through `PeachDrawing.Text.Layout`

The paragraph layout API (`ParagraphBuilder`, `Paragraph`, `ParagraphLayout`, and the host-driven `LineFlow`) does everything the first slice was missing: line breaking (UAX #14 with the CSS
tailorings), bidi reordering, alignment, justification, carets, font fallback, spacing, `text-indent`, tab stops, hyphenation, a line limit with an ellipsis, and inline boxes. PeachPDF still
does not consume it, and that is deliberate rather than pending: `FlowBox` interleaves the wrap decision with float intersection, speculative line-height growth, fragmentainer break tokens
and `::first-line`, so a wholesale replacement is not planned. The library owns "break the next line given an available range" (`LineFlow.TryNext`, which never changes its inputs, so the
caller's own undo is a call with an earlier cursor) for embedders without those constraints; PeachPDF stays the host of its own retry loop.

The pure helpers PeachPDF has for the same jobs are written over its word model (`CssRect` words, `CssLineBox`), not over text, so they do not move behind the library:
`ExpandTabs` and `GetLineTextIndent` (tab stops and indent, kept in PeachPDF with their tests), `ApplyJustifyAlignment` and `IsJustificationOpportunity`, the overflow-wrap candidate search,
the hyphenation candidate predicates, and the three copies of bidi line reordering. Moving one would mean rewriting its characterization tests, which are what keep the two layouts agreeing.

Tracked in [#1417](https://github.com/jhaygood86/PeachPDF/issues/1417) until the API closed it.
