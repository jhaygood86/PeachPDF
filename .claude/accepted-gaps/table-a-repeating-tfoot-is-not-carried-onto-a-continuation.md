# A repeating `<tfoot>` is not carried onto a page a row continues onto

_Tracked as [#493](https://github.com/jhaygood86/PeachPDF/issues/493)._

[css-tables-3 §6.2](https://www.w3.org/TR/css-tables-3/#repeated-headers) repeats a `<tfoot>` at the
bottom of every page a table spans, on the same terms as a `<thead>` at the top — *"When the header rows
are being repeated, user agents must leave room and if needed render the table top border. The same
applies for footer rows and the table bottom border."* PeachPDF repeats it when the break falls
**between two rows** and draws it **once**, under the table's last row, when the table spans pages
because a *cell* continued. Measured on `LayoutHarness`, 300pt page, 20pt margin, occurrences per
fragmentainer:

| shape | pages | `<tfoot>` | `<thead>` |
|---|---|---|---|
| 40 short rows | 3 | `1, 1, 1` | `1, 1, 1` |
| one row, 244-word cell | 7 | `0, 0, 0, 0, 0, 0, 1` | `1, 1, 1, 1, 1, 1, 1` |

## Why it was not fixed with the `<thead>` half

[#439](https://github.com/jhaygood86/PeachPDF/issues/439) needed the header to be *positioned*
correctly — the proxy was already being created on a continuation. The footer is not being created at
all, and the two gates that would have to change are both load-bearing:
`CssLayoutEngineTable.LayoutBodyRows`' per-row break block is guarded `i > ResumeRowIndex` because
re-deciding the resumed row's break point takes a forced break twice, and step 5's closing footer is
gated `!cursor.Stopped` because a pass that has not reached the last row would put the closing footer
in the middle of the table on the page it is leaving (measured during
[#464](https://github.com/jhaygood86/PeachPDF/issues/464) at y=36.5 under a row ending at 35.0). What
is missing is a third case rather than a relaxation of either — a pass that stops owing the page it
leaves a footer at that page's bottom.

## What the fix will have to get right

The footer's room has to be reserved from the **bottom** of the band the way the header's is now
reserved from the top (`FragmentainerContext.ResumeContentInset`). Without that it repeats #439's
defect mirrored: the footer drawn over a continuation that already flowed into that space, every word
still claimed by exactly one fragmentainer, and nothing wrong anywhere in the fragment tree. See
[the invariant](../invariants/fragmentation-a-repeated-groups-room-is-owed-to-the-flow-the-row-cursor-cannot-position.md)
for why the per-word check cannot see this.

Stated reader-facing under [Page Breaks](../../docs/html-css-support.md#page-breaks).
