# A box's resumption record is readable only at the instant its layout returns

_CSS Fragmentation Level 3 §2. Tracker: [#390](https://github.com/jhaygood86/PeachPDF/issues/390)._

`CssBox.BeginLayoutPass` clears `PendingBreakToken` at the top of **every** `PerformLayoutImp`, so a
box's record says where it stopped only until the next layout of that same box. Anything that wants
the answer has to ask at the call site — inside the loop that laid the child out, between
`await child.PerformLayout(g)` returning and anything else touching the child.

Measured, so the shape of the wrong answer is recognizable: a `<div>` of 600 words on a 300pt page
takes **16 fragmentainer passes**, records a break on fifteen of them, and afterwards **zero boxes in
the whole tree carry a record** — the box that stopped fifteen times reports `null`. A consumer that
gathers records after layout, or in an epilogue, or from a second walk of the tree, is not reading a
weaker version of the answer; it is reading the absence of one, silently and without an exception.

This is what made `CssLayoutEngineTable.LayoutBodyRow` the only possible home for the table's own
consumer (`TableRowCursor.RecordIfUnfinished`): the row loop's `foreach` is the last frame that names
a cell at all, and the engine's whole-table pre-checks can restart that loop and lay every cell out
again, so a record collected any later is a record from some other attempt.

The sibling rule about what a box's *geometry* means while it carries a record is
[a box's own measurements are only valid at specific times](fragmentation-a-boxs-own-measurements-are-only-valid-at-specific-times.md).
