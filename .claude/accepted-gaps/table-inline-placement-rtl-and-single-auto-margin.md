# A `<table>` does not end-align in an rtl container, and a single `auto` margin does not right-align it (#1251)

Left out of the block-placement fix on purpose; tracked as a spec deviation in #1251, not accepted forever.
Read [.claude/recent-fixes/2026-09-21-inline-placement-of-a-narrow-block-rtl-and-single-auto-margin.md](../recent-fixes/2026-09-21-inline-placement-of-a-narrow-block-rtl-and-single-auto-margin.md)
first: it is the mechanism this gap is the boundary of.

A table narrower than its containing block always sits at the left edge, except with both margins `auto`
(which centres correctly). Chrome 153 vs PeachPDF, in pt within a 200pt container:

| declaration | Chrome | PeachPDF |
| --- | --- | --- |
| `direction: rtl`, `width: 100pt` | 100..200 | 0..100 |
| `direction: ltr`, `width: 100pt; margin-left: auto` | 100..200 | 0..100 |

## Why it is not covered by the block fix

`CssLayoutEngine.IsInFlowBlockLevel` admits only `display: block` and `list-item`, and
`CssBox.ResolveBlockInlineStart` is only reached through the block placement path. A table is positioned by
`CssLayoutEngineTable`, and `GetActualMarginLeft/Right` return 0 for one unless that engine passes the resolved
`boxWidth` - which only the both-`auto` branch uses. Extending the rule means resolving the table's border-box
width at the point the offset is committed, which is a change to the table engine rather than to the block
frame, and it was not measured.

Deleted together with the limitation sentences in the `margin` and `direction` rows of
`docs/html-css-support.md` when #1251 closes.
