# `float: inline-footnote` and column-scoped footnote variants not supported

css-gcpm-3 defines `float: footnote` alongside `float: inline-footnote` (the footnote's content is
rendered inline, immediately after the paragraph it appears in, rather than routed to a page-level
note area) and lets a multi-column container define its own, column-scoped footnote area rather than
the page's. PeachPDF implements only the page-level `float: footnote` case: `Floating.Footnote`
(`CSS/Enumerations/Floating.cs`) is a single keyword with no column-scoping concept, and
`DomParser.DetachFootnoteBodies`/`HtmlContainerInt.ResolveFootnotesForThisAttempt` reserve room and
render bodies against the *page's* own content band (`HtmlContainerInt.PageBottomOf`), never a
column's. `float: inline-footnote` is not a recognized keyword at all - declaring it is dropped as an
invalid value (falls back to `float: none`, the property's initial value).

Filed as [issue #749](https://github.com/jhaygood86/PeachPDF/issues/749).
