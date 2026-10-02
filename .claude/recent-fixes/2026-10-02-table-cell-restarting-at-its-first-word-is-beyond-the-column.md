# A table cell that restarts at its first word was still drawn where it had been laid out first (seed 15)

#1573 made a column run forget what its later column moved on whole, by reading the carried record: a block link that breaks
before a child names that child and everything after it as beyond, and a table link names each unfinished cell's own
token. A cell whose token is an `InlineBreakToken` at word 0 with no lines done is the other way a cell restarts: its
own flow holds the content, there is no child to name, and the walk stopped at the inline record (its words are "already said per
word"). So the cell stayed in the earlier column's snapshot, and the table, which the record restarts from that row, laid it
out again on the next page: the word was in two fragments.

Fix: in `AddBoxesBeyond`, a table's unfinished cell whose token is `InlineBreakToken { ResumeWordIndex: 0, CompletedLineCount: 0 }`
is itself beyond (the cell and its descendants are forgotten), the same as the cell that breaks before its first child.

Reduction: `columns:2` > `<table>` holding a bare block of 16 words and a `<td>` (CSS anonymous cell and row around the block, then the
real cell), after a 27pt spacer. Proper `<tr>`/`<td>` tables did not reproduce it, because the anonymous cell is the one that
breaks before its first child while the real cell's flow restarts at word 0.

Evidence: corpus (300 documents, page-edge visibility): doubled words 6 to 0 (seed 15), lost words unchanged at 2, nothing else
moves; net8.0 suite green; the new test fails on main.