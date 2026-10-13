# Finding a child's place among its siblings stopped being a search from the start

The profile of `table-l` (one table, 30,000 rows) put 18% of its CPU in `CssBox.CountPrecedingRegionalIndicators` and 12% in
`FragmentEmitter.HoldsARowspanContinuation`. Neither does much work of its own; both were quadratic in the number of rows.

**`GetPreviousSibling` and its neighbours searched `Boxes` from the start.** `DomUtils.GetPreviousSibling(box)` did
`Parent.Boxes.IndexOf(box)`, and the regional-indicator walk calls it for the first text of every row, which climbs to the `tr` and asks
for its place among the 30,000 (plus white-space boxes between them). `CssBox.IndexOfChild` now looks first near where the last lookup
on that parent found a child (a hint kept on the parent, eight positions each way, because rows are separated by boxes of their own),
and falls back to `IndexOf`. Walks in document order, which these are, never fall back. Applied to the `IndexOf(this)` call sites
that are asked about every box in turn (`GetPreviousSibling`, `GetFollowingSiblings`, the three `reference.ParentBox.Boxes.IndexOf`
walks in `DomUtils`, `CssLayoutEngine`'s containing-box lookup, the counter and quote engines, `FoldOwnTrailingBlockMargin`,
`LiftDisplayContentsChildren`). The other `IndexOf` calls ask once per box in unusual cases and were left.

**`HoldsARowspanContinuation` scanned a box's children for a `CssSpacingBox` once per slot per child.** It now reads
`CssBox.HoldsSpacingBox`, set where table layout inserts a `CssSpacingBox` (two sites in `CssLayoutEngineTable`). A stale `true`
would only stop a skip, never allow a wrong one. The call is also placed after the cheap geometry test in the skip condition.

Measured in process, best of 4, interleaved with the build before: `table-l` 12.1 -> 8.5-9.3 s, `prose-l` 7.0-7.3 -> 5.8-6.5 s,
`multicol-l` 11.6-14.4 -> 9.1-10.4 s, `invoices-l` unchanged; allocation identical. Output is unchanged: PyMuPDF words and drawings
match the previous build on the 19 benchmark documents and 199 of 200 fuzz documents (the 200th is a known case).

Not done: `UnicodeLineBreaks.Find` still receives a count of regional indicators computed for every text. It could be asked only
when needed, but the break opportunity at the first position of a text also depends on the odd/even state (an open flag puts a
regional indicator before it), so skipping it for texts that do not start with one changes the answer at index 0.
