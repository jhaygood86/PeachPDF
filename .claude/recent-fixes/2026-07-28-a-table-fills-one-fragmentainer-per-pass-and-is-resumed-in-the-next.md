# A table fills one fragmentainer per pass, and is resumed in the next

_Landed 2026-07-28._

[#464](https://github.com/jhaygood86/PeachPDF/issues/464), and with it
[#390](https://github.com/jhaygood86/PeachPDF/issues/390) stage 4's first non-neutral step. The table
engine's row loop has been able to stop where a cell stopped, record where, and consume that record
since [#466](2026-07-27-the-table-row-loop-stops-where-a-cell-stopped-and-a-later-pass-continues-it.md)
— but nothing handed it one, so no second fragmentainer pass was ever opened and **the rows after the
stop were never placed**. Two lines close that: the record is published as the table's own
`PendingBreakToken`, and `CssBox.LayoutMonolithicContent` no longer wraps the engine. The method is
**deleted** — the table was its only caller — and the table arm now goes through `LayoutEngineContent`,
which flex and grid already used, with a `BreakToken?` parameter added for the one engine that reads
one.

## The load-bearing finding, which is not what was predicted

[PR #481](2026-07-27-two-of-the-things-that-must-be-true-before-a-table-publishes-its-record.md)
measured the gate move in a scratch tree and left two duplications behind, attributing both to
"`CssBox`'s generic block-resume path re-placing a resumed cell's block child". That reading was
wrong on both counts.

**The `<p>`-in-`<td>` duplication was `ApplyCellVerticalAlignment`, not the block-resume path.** A
cell that resumed from a carried record *and finished on that pass* is in neither `stoppedCells` nor
`TableBreakToken.FinishedCells`, so nothing excluded it from the row's alignment. `dist` comes out
large and positive — the cell's `ClientBottom` names the continuation's row while `GetMaximumBottom`
walks live boxes whose kept-in-place child still reports the page it began on — and `OffsetTop` then
deep-moved the whole subtree, **including words the first page had already frozen a fragment around**.
The `<p>` went from Y 22.7 to 235.3 between passes, which put one line across the 290pt boundary and
had both bands claim its 14 words. The block-resume path never re-placed anything: `<p>.Location` was
untouched by it on every pass, which is what the trace showed and reading could not.

The fix is the same statement #481 already made for a stopped cell, widened by one case: **only a
fragment that both opens and closes in this fragmentainer has room of its own to distribute.** Now
[an invariant](../invariants/fragmentation-a-continuation-may-not-move-geometry-an-earlier-fragmentainer-emitted.md).

**The repeating-`<thead>` "1 word twice" was never a defect at all.** The header's `CssRect` is
claimed by slots 0, 1 and 2 at three *different* fragmentainer-local positions — one shared subtree
drawn on every page, which is what a repeating header is. It appeared once before because the header
**did not repeat** when the break fell inside a cell, which is
[#439](https://github.com/jhaygood86/PeachPDF/issues/439). Reading a reference-identity word count as
a duplication check made a fixed defect look like a new one.

## The guard that could not be falsified, and now can

#464's second precondition — discard line boxes a resumed flow holds past its record's
`CompletedLineCount` — was built by #481 and dropped, because no test could tell it from its own
absence. **Moving the gate produced the case.** `<div style='column-count:2'><table><tr><td>{244
words}</td></tr></table></div>` throws `ArgumentException: An item with the same key has already been
added` out of `CssLineBox.AssignRectanglesToBoxes`: the columns engine abandons a fill attempt, the
table is laid out again over cells still holding that attempt's lines, and the record still names the
earlier count. `CssLayoutEngine.CreateLineBoxes` now calls `CssBox.DiscardLineBoxesFrom` on every
resumed flow — unconditional rather than guarded, because a record naming line *n* was written by the
pass that finalized lines 0..*n*−1, so nothing an earlier fragmentainer emitted can be in the range it
drops. [An invariant](../invariants/fragmentation-a-resumed-flow-may-hold-more-lines-than-its-record-accounts-for.md),
with that markup named as the sweep that fails if it is removed.

**What this says about the general shape:** a guard that cannot be falsified is often waiting on a
reachable *case* rather than on a cleverer test. #481 was right to drop it and right to record the
reproduction attempt; the thing that changed was the code around it, not the guard.

## The two-cycle, which only a spinning test found

A test double whose cell never finishes took **100,000 passes and 1m52s** — the driver's pass cap,
not its no-progress backstop. Two independent causes, and the second is a real content defect that
the first was hiding.

**`FinishedCells` was not carried forward, so the answer oscillated.** A continuation places nothing
at all for a cell that finished earlier, so `RecordIfUnfinished` never sees it and the record that
pass publishes does not mention it. The pass after that is therefore not told, enters the cell from
the start, and re-places content two fragmentainers back. Measured as the record alternating between
naming one finished cell and none, forever. "Finished" is a fact about the cell rather than about one
pass, and the row loop cannot re-derive it — the cell's own `PendingBreakToken` is whatever some
earlier layout left behind — so the skip arm now says so explicitly
(`TableRowCursor.CarryForwardFinished`). **This is the same class of defect `FinishedCells` was
introduced for in #481, one pass further out.**

**The backstop could not see a table that got nowhere.** `HtmlContainerInt.LayoutDocument` ends a run
that hands back the record it was given, and that check is an equality test. A `record`'s generated
equality compares members with `EqualityComparer`, so `TableBreakToken`'s three collections were
compared **by reference** — and every pass builds fresh ones, so a table never compared equal to
itself. `TableBreakToken` now has a contents-based `Equals`/`GetHashCode`. Note the two faults
compounded: the backstop compares *consecutive* passes, so even with correct equality a two-cycle has
no two equal records in a row. Both had to be fixed for the run to end, and each alone looks like it
did nothing.

With both: **3 passes, `LastResortRelayouts == 1`, 1s.** Pinned by counts rather than by elapsed time,
per [the testing invariant](../invariants/testing-read-the-test-count-not-the-word.md)'s neighbours —
a clock would only ever have said "slow".

## The two the showcase corpus caught, which no unit test would have

The suite was green and every probe clean while **the whole table was missing from the first page** of
`paged_media_table_cell_lines`. Both causes were only visible end to end.

**A resumed cell must keep the `Location` its first fragment was built from.** `LayoutBodyRow` wrote
this pass's row top into every cell it placed, including one continuing from an earlier
fragmentainer — and a `CssBox` has exactly one `Location`, so that retracts the earlier fragment's
geometry. The emitter, notified the box moved, rebuilt that fragmentainer from where the box is *now*
and found nothing of it there: 149 of 240 words gone, borders and all, with the second page perfect.
The fix is the rule every other box already follows — `CssBox.ResumeInTheNextFragmentainer` moves a
box only inside a fragmentainer with a band of its own, never on the page grid — and where this pass's
content goes is the flow's question, which `CreateLineBoxes` already answers from the fragmentainer's
own content top.

**A line that ends at a fragmentation break is not the block's last line.**
[CSS Text §7.3](https://www.w3.org/TR/css-text-3/#text-align-property) exempts the last line of a
block from `text-align: justify`, and `ApplyJustifyAlignment` read that off `LineBoxes[^1]`. Once a
block's flow could stop mid-block, the line the pass stops on *is* the last one the list holds, so
every line at a page or column boundary stopped being justified. `FinalizeLineBoxes` now takes whether
the flow finished. This is a **correction beyond the table**: it is why `multicol` is the one showcase
whose pixels change, and both PDFium and MuPDF agree the last line of each column fragment is now
justified to the column's full width.

## Measured, `main` vs this change, same tree

The probe was re-run with `git stash push -- src/PeachPDF` so both columns are the same fixtures on
the same machine. 244 words, 200×300pt page, 10pt margin, `line-height: 18pt`. **The line height is
load-bearing in the fixture**: at 20pt the lines divide the 280pt band exactly, no line ever
straddles, and every shape silently paginates by word relocation without exercising a break token at
all — a rig that measures nothing while looking like it measures everything.

| fixture | `main` | this change |
|---|---|---|
| bare `<td>` | 244 once, **1 pass** | 244 once, **3 passes** |
| `<p>` in `<td>` | 244 once | 244 once |
| two rows | 245 once | 245 once |
| two cells, one short | 245 once | 245 once |
| **multicol in a `<td>`** | 234 once, **10 twice**, 5 pages | **244 once, 0 twice**, 3 pages |
| **repeating `<thead>`** | header on **1** page | header on **all 3** |
| table in a multicol | 244 once, 6 pages | 244 once, 3 pages |
| flex in `<td>` / grid in `<td>` | 6 twice each | **identical** |
| `rowspan` | 244 twice, all in slot 0 | **identical** |

So this also closes [#430](https://github.com/jhaygood86/PeachPDF/issues/430) (a multicol inside a
cell losing content). The last two rows are **pre-existing and unchanged**, which is why they are in a
separate theory: the flex/grid one is the documented "a page boundary can cut through a line of a flex
or grid item" limitation, and the `rowspan` one is a `CssSpacingBox` placeholder emitting the same
subtree once per spanned row into one fragmentainer. Neither is this change's to claim either way.

**[#439](https://github.com/jhaygood86/PeachPDF/issues/439) is improved but not closed, and the
showcase is why.** A repeating `<thead>` now repeats above a continuation in the harness — pinned by
`ARepeatingHeader_RepeatsWhenTheBreakFallsInsideACell` over a one-cell and a two-cell row — but the
`paged_media_table_row_continuation` showcase, which goes through the real `PdfGenerator` with an
`@page` rule, still shows its header once. Two green tests were not enough to know that; only
rendering the thing and looking was. Leaving #439 open on the strength of the showcase rather than
closing it on the strength of the tests is the honest reading, and the showcase's own description says
so rather than claiming a header that does not appear.

## Deliberately not done

- **[#432](https://github.com/jhaygood86/PeachPDF/issues/432) is untouched.** The row loop's band is
  still a counter, and
  [the stale-cursor invariant](../invariants/fragmentation-a-stale-cursor-can-be-load-bearing-compensation-for-a-bad-estimate.md)
  says why correcting that alone regresses four tests. It was waiting on this change, not fixed by it.
- **[#478](https://github.com/jhaygood86/PeachPDF/issues/478) is now reachable rather than closed.** A
  finished cell contributes no fragment to its row's continuation, so its borders stop at the page
  boundary instead of running the continuation's depth. Its gap file's "not visible today" section is
  updated, and the limitation is stated reader-facing in `docs/html-css-support.md`.
- **The flex/grid and `rowspan` duplications above.** Measured identical to `main`; chasing them here
  would have hidden which numbers this change is responsible for.

## Evidence

Full net8.0 suite green (6,837 tests), CLI suite green (96);
`dotnet build PeachPDF.slnx -t:Rebuild` with zero warnings. **68 of 69 showcases byte-identical** to
`main` after normalizing creation date, `/ID`, subset tags and the annotation `/M` and `/NM` (the last
is a per-run GUID, and normalizing it is what reduced an apparent five-showcase diff to none).
`multicol` is the one that changes, for the justification correction above, agreed by both renderers.
One showcase is new.

Tests: `TableCellBreakTokenTests`'s obsolete
`APaginatingTable_RecordsNoUnfinishedCell` became `APaginatingTable_ClaimsEveryWordExactlyOnce` over
the seven shapes that can satisfy it plus `APaginatingTable_DropsNoWord` over the three that cannot,
and `ARepeatingHeader_RepeatsWhenTheBreakFallsInsideACell`;
`TableRowLoopResumptionTests.ACellResumedFromAnEarlierPass_IsNotAlignedAgainstTheRowItFinishesIn` with
its control `ACellTheContinuationEntersFresh_IsStillVerticallyAligned`, plus
`ACellResumedFromAnEarlierPass_KeepsTheLocationItsFirstFragmentWasBuiltFrom` and its control; new
`JustifiedLineAtABreakTests` (three, including a block that does not break at all, so the two
boundary assertions cannot both pass on a fixture that never justifies). New showcase:
`paged_media_table_row_continuation`.

**What a future change should take from this:** every defect here was found by the corpus or by a
spinning clock, not by the suite, and each was invisible to the probe that measured word claims. A
per-word "claimed exactly once" check says nothing about a fragment that was never emitted, and a
green suite says nothing about a page that came out blank.
