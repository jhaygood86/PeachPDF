# Footnote detachment can lose SVG/MathML foreign-content ancestry for case-sensitivity purposes

`DomParser.DetachFootnoteBodies` walks the whole box tree for `float: footnote` sources and severs each
one's `ParentBox` before cascading its marker/call boxes - unlike every other restructuring pass in
`DomParser.cs`, it has no `if (box is CssBoxSvg or CssBoxMath) return;` guard. Since #1384's fix,
`CssBox.IsWithinForeignContent` (consulted by `NameComparison`/`GetAttribute` for case-sensitivity) is a
snapshot taken from a box's ancestry at construction time, so a `float: footnote` element inside
`<svg><foreignObject>` that gets detached loses that ancestry before its footnote marker/call boxes are
selector-matched, and falls back to ordinary case-insensitive HTML matching.

**Why it stays:** this needs a narrow, unusual combination (`float: footnote` + inline `<svg>` +
`<foreignObject>` + HTML content + a case-sensitivity-dependent selector) with no existing test coverage
either way. Fixing it also means deciding what case-sensitivity a footnote body should have once relocated
out of a `<foreignObject>` into the page's footnote area - arguably ordinary HTML again, in which case
today's behavior is actually correct - a design question distinct from #1384's scope. Tracked in issue
#1507.
