# Nested `float: footnote` is inert, not an error

`DomParser.DetachFootnoteBodies` never recurses into a footnote body it has just detached (see the
`continue` in its own tree walk) - so a `float: footnote` box authored *inside* another footnote's body
is never discovered, never numbered, and never routed to a note area. It stays exactly where it was
written, as ordinary content of the outer footnote's body: not floated (`CssBox.IsFloated` only
recognizes `left`/`right`), not blockified (`DerivedStyle.ActualDisplay`'s own float exclusion covers
`Floating.Footnote` too), simply inert.

This falls out of the detection walk's own shape rather than being a deliberately engineered restriction
- css-gcpm-3 does not obviously define what a nested footnote should mean in the first place (would it
share the outer footnote's number, its own separate counter, its own note area on the same page?), so
"inert" is a defensible default pending real spec guidance rather than a gap to close outright. Covered by
`FootnoteIntegrationTests.Footnote_Nested_IsInert`.

Filed as [issue #751](https://github.com/jhaygood86/PeachPDF/issues/751).
