# A fragmented table row re-enters the cells that already finished

_Tracked as [#464](https://github.com/jhaygood86/PeachPDF/issues/464)._

[css-tables-3 §6.1](https://www.w3.org/TR/css-tables-3/#fragmentation) says a fragmented row fits as
much content as it can in each of its cells **independently**, and the next fragment starts each cell
where *that* cell stopped. The cells of one row are
[css-break-3 §2.1](https://www.w3.org/TR/css-break-3/#parallel-flows) parallel flows, so a row can
stop with some of its cells finished and others not.

`CssLayoutEngineTable`'s row loop records exactly that — `CssBox.TableContinuation` carries one
`UnfinishedTableCell` per cell that stopped — and a continuation hands each of those cells its own
record back. **The cells that finished are entered from the start**, because a cell that finished is
simply absent from the record and there is nothing to distinguish it from a cell being laid out for
the first time. Their content is therefore laid out again on the continuation's page, on top of the
fragment the earlier pass already emitted.

## Why this is not fixed here

Distinguishing the two needs the fragment model, not another field on the record: a finished cell has
to produce an **empty** fragment in the continuation's fragmentainer rather than no fragment, so the
row's height and the table's borders still account for it while none of its content is drawn a second
time. That is the same question `box-decoration-break` and the §6.2 unbroken strip already ask of a
box that spans fragmentainers, and it belongs with the step that makes a table resumable at all.

## Why it is not reachable today

Nothing sets a table's own `PendingBreakToken`, so no fragmentation pass ever hands a table a
resumption record — the engine's `resume` parameter is null for every document. The continuation path
exists and is tested, but only a test can reach it. See #464 for the measurements and for the two
other things that must be true before the record is handed back (the monolithic gate, and a cell
resumed on an inconsistent record throwing in `CssLineBox.AssignRectanglesToBoxes`).
