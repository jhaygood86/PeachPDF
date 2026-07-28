# A finished cell produces no fragment on its row's continuation, rather than an empty one

_Tracked as [#478](https://github.com/jhaygood86/PeachPDF/issues/478)._

[css-tables-3 §6.1](https://www.w3.org/TR/css-tables-3/#fragmentation) fragments a row by fitting as
much as each of its cells can take **independently** — the cells of one row are
[css-break-3 §2.1](https://www.w3.org/TR/css-break-3/#parallel-flows) parallel flows, so a row can
stop with some cells finished and others not. A cell that finished has its whole content in the
fragment the earlier pass emitted; the row's *box* nevertheless continues into the next
fragmentainer, and §6.1 has the cell's box continue with it. In a browser that shows up as the
finished cell's borders and background running the full depth of the row's continuation fragment,
with no content in it.

`CssLayoutEngineTable` now knows which cells finished — `TableBreakToken.FinishedCells`, matched by
reference — and a continuation **places nothing at all** for one: not its position, not its content,
not its vertical alignment. Only the column cursor moves past it, so the cells beside it keep their
columns. That is what stops the earlier pass's content being re-placed onto the continuation's page.

**What is not done is the other half: the cell contributes no fragment there.** Its borders and
background stop at the fragmentainer boundary rather than continuing to the bottom of the row's
continuation fragment, and the row's height on that fragmentainer is decided by the cells that
continue into it alone.

## Why it is not fixed here

An empty fragment is not something the row loop can produce by placing a box: a `CssBox` carries one
`Location`, and the finished cell's has to keep describing the fragmentainer it was placed in. What
§6.1 asks for is a *second* fragment for the same box, holding the box's geometry in this
fragmentainer and none of its content — which is the same question `box-decoration-break` and §6.2's
unbroken strip already ask, and it belongs in `FragmentEmitter` rather than in another field on the
record.

## It is visible now

It was not, while nothing set a table's own `PendingBreakToken`: no pass handed a table a resumption
record, and the continuation path was reachable only from a test.
[#464](https://github.com/jhaygood86/PeachPDF/issues/464) closed that, so a `<td>` whose content
overflows a page reaches this from ordinary markup. The half that landed with it is the one whose
absence would *duplicate* content; this half only under-decorates, which is why the two could be
separated at all.

Stated reader-facing under [Breaks between table rows](../../docs/html-css-support.md#breaks-in-tables).
