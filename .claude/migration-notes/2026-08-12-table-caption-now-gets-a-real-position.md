# `<caption>` inside a `<table>` now gets a real position, and `caption-side` now works

Previously: a `<caption>` element inside a `<table>` was never assigned a position by
`CssLayoutEngineTable` at all — it kept whatever degenerate zero-size geometry an unlaid-out box starts
with, so it did not reliably appear above the table (or anywhere in particular). The `caption-side`
CSS property parsed successfully but had no effect on layout.

Now: a `<caption>` is laid out as a full-width block stacked above the table's row grid
(`caption-side: top`, the initial value — CSS 2.1 §17.4) or below it (`caption-side: bottom`), matching
the table's own content width. Any existing document with a `<caption>` will now render it in that
position instead of not rendering it correctly. Both the caption and the row grid are painted inside the
same box the `<table>` element's own `border`/`background-color` apply to, so a bordered or filled table
now visually encloses the caption too — see the accepted-gap note on this (tracked as
[#721](https://github.com/jhaygood86/PeachPDF/issues/721)) for the one respect in which this isn't full
CSS 2.1 §17.4 conformance (a separate "table wrapper box" that the caption sits outside of).
