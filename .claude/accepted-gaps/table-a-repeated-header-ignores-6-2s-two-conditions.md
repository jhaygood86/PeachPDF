# A repeated `<thead>`/`<tfoot>` ignores css-tables-3 §6.2's two conditions

_Tracked as [#494](https://github.com/jhaygood86/PeachPDF/issues/494)._

[css-tables-3 §6.2](https://www.w3.org/TR/css-tables-3/#repeated-headers) makes repetition
**conditional**, and PeachPDF applies neither of the two middle conditions:

> …user agents must repeat header rows and footer rows on each page spanned by a table if the page is
> the table's fragmentainer, **if the header/footer has avoid `break-inside` applied to it**, **if the
> height required to do so is inferior to two quarters of the page height** (up to one quarter for
> header rows, and up to one quarter for footer rows), and if that doesn't cause a row to be displayed
> twice on that page.

`CssLayoutEngineTable._shouldRepeatHeaders` is `_headerBox != null && _headerBox.Display ==
table-header-group` — the existence of a `<thead>`, nothing more. So a header with no
`break-inside: avoid` repeats when the spec says it should flow once, and a header taller than a
quarter of the band repeats with nothing capping it.

## Why it was not fixed with #439

[#439](https://github.com/jhaygood86/PeachPDF/issues/439) was about the *other* sentence in the same
section — "user agents must **leave room**" — which is now honoured for a mid-cell continuation. The
conditions are a separate question, and the `break-inside` half is not a small one: unconditional
repetition is what every existing repeating-header test and the `table_header_repeat` and
`paged_media_table_row_breaks` showcases assert, so applying it changes behaviour for every document
with a `<thead>`. That wants checking against what browsers actually print, not just against the
prose.

**This gap became more expensive when #439 was fixed**, which is why it is written down now. Room was
not previously reserved against a continuing cell — the header was drawn over it — so a tall repeated
header cost nothing. Now it costs its own height out of every band the table spans, which is correct
and is exactly what the quarter cap exists to bound.

The quarter-height cap alone is local — the band height is already asked for as
`container.PageBandHeightOf(slot)`, and `_headerHeight`/`_footerHeight` are settled once per table
before the row loop — so splitting the two is likely the right move.

Stated reader-facing under [Page Breaks](../../docs/html-css-support.md#page-breaks).
