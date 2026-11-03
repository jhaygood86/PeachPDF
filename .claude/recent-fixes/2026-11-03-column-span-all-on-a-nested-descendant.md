# `column-span: all` works on a nested descendant (#625)

**Idea.** The columns engine only partitions the container's own children (`BuildSegments`) and its break hooks are
guarded to the container's own loop, and propagating a "column-span handoff" up through every ancestor's break token
was the costly path the old gap file described. Instead the box tree is split at parse time:
`DomParser.HoistNestedColumnSpanners` (after `CorrectAnonymousTables`) finds a spanner reached through plain in-flow
blocks and splits each ancestor into a before piece and an after piece (`InheritStyle(everything: true)`, as the inline
split does), hoisting the spanner between them as a direct child. The existing segment machinery then does the rest.

**Slice decoration.** The before piece keeps the top border/padding/margin and loses the bottom; the after piece the
reverse (the split is `box-decoration-break: slice`). A piece with nothing in it is dropped, so an ancestor with nothing
after the spanner keeps its bottom decoration where it was.

**Eligibility.** Only `display: block` ancestors that do not establish an independent formatting context or a multicol
container (list items, tables, flex/grid, floats, inlines and non-visible overflow stop the search), which is the spec's
"no effect outside the container's own block flow".

**Not done / traps.** Counters and `id`s are copied with the style, so a split ancestor's `counter-increment` fires once
per piece and its `id` exists twice (first wins for links); the accessibility/structure tree shows the ancestor twice.

**Evidence.** Three new `MulticolLayoutIntegrationTests` (nested spanner, deeply nested with slice borders, no effect behind
`overflow: hidden`); full engine, PeachPDF and PeachBrackets suites green.
