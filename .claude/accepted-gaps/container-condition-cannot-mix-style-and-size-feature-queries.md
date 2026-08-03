# A single `@container` condition can't mix `style()` and size-feature queries

CSS Containment 3's `<container-condition>` grammar lets `style(...)` queries and plain size-feature
queries (`(min-width: ...)`) both appear as `<query-in-parens>` alternatives in the same condition,
combined with `and`/`or`/`not` at the top level — e.g.
`@container (min-width: 300px) and style(--theme: dark) { ... }` is valid per spec.

`StylesheetComposer.CreateContainer` decides, once, whether a rule's whole condition is a size-feature
`MediaList` or a `style()` query tree, based on whether the token immediately following the optional
`<container-name>` is a `style(` function call — the two forms are mutually exclusive per rule. A
condition that mixes both at the top level (a leading size feature followed by `and style(...)`, or
vice versa) is not supported.

Real-world `@container` usage is overwhelmingly a single query form per rule. Tracked as
[#618](https://github.com/jhaygood86/PeachPDF/issues/618).
