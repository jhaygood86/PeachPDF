# A detached fragmentainer does not stop a nested engine from recording a break

`CssBox.LayoutMonolithicContent` detaches the fragmentainer around the table engine, and the natural
reading of that is "nothing inside a cell has a fragmentainer to run out of, so no cell can carry a
resumption record". [#452](https://github.com/jhaygood86/PeachPDF/issues/452) measured that reading
over ten table shapes — bare `<td>`, `<p>`-in-`<td>`, two rows, repeating `<thead>`, repeating
`<tfoot>`, `column-count` in a `<td>`, `display:flex` in a `<td>`, `display:grid` in a `<td>`, a
`rowspan` cell, and a table nested in a `column-count` container — and not one produced a record.

**It is not true in general, and one fixture in the suite disproves it.**
`MulticolLayoutIntegrationTests.InsideAnotherEngine_NoContentIsDropped(outerStyle: "display:table")` —
a `columns: 2` container inside a `display: table`, twelve 40px items on a 120pt page — produces a
`<td>` carrying a `BlockBreakToken`. `CssLayoutEngineColumns` establishes a fragmentation context of
its **own** inside the detached scope, so `CurrentFragmentainer is { HasOwnBand: true }` while
`HtmlContainerInt.IsFragmenting` is false, and a column running out is a break like any other. The
same chain is what [#430](https://github.com/jhaygood86/PeachPDF/issues/430) traces from the other
end.

## What this means for a change

**Detaching suppresses the *page* vehicle, not breaking.** Two questions, and the code has had a
[named distinction](fragmentation-monolithic-is-two-questions-and-so-are-the-gates-around-it.md) for
them since #400 (c). An engine that drives fragmentainers of its own answers the second for itself
whatever the first says, so "my new code path is unreachable because the fragmentainer is detached"
is a claim about the *page* grid only, and any nested engine below the detach — multi-column today,
whatever drives its own tomorrow — can reach it.

**A sweep over table shapes is not a sweep over the reachable set**, which is why #452's ten fixtures
missed it: every one of them nests the interesting content directly in a `<td>`, and the fixture that
reaches it nests a *container that paginates* there. Measure by probing the call site over the whole
suite and the whole showcase corpus, not by enumerating shapes.

## The measured symptom

A probe recording every entry into the row loop's new stop, run over the 6,730-test suite and all 69
showcases: the showcases never reach it, the suite reaches it **once**, from that fixture. It is
harmless there only because the row that stops is the table's last, so stopping the loop skips no
row — a fact about the fixture, not about the guard.
