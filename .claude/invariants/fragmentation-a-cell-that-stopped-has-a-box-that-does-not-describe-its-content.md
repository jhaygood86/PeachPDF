# A table cell that stopped has a box that does not describe its content

`CssLayoutEngine.CreateLineBoxes` sets `blockBox.ActualBottom` **after** the flow finishes. A flow that
runs out of fragmentainer returns on the break *before* that line, so a cell that stopped comes back
holding the `ActualBottom` its placement gave it — its own top (`CssBox.LayoutContents` sets
`ActualBottom = Location.Y` before opening the flow) — while its lines sit anywhere between that top
and the bottom of the band.

**Anything that reads a cell's box as its height has to ask whether the cell finished first.** Two
things in `CssLayoutEngineTable.LayoutBodyRow` do:

- the row's own `MaxBottom`, which is what the next row starts below and what the table's slice bottom
  is recorded from; and
- `CssLayoutEngine.ApplyCellVerticalAlignment`, which distributes `cell.ClientBottom - contentBottom`
  over the cell's children — a **negative** number when the box is the degenerate one, so the whole
  fragment is pushed *up* by half the content's depth.

Measured with the monolithic gate lifted so a real `<td>` could stop (the #464 probe, 244 words on a
300pt page): the cell's first line landed at document Y **−104**, nearly a page and a half above the
document origin, and 121 of its 244 words were emitted. Skipping the alignment for a cell that
stopped, and taking its bottom from `CssBox.GetMaximumBottom` instead, put every one of the 244 back.

The spec side is the simpler statement and the one to reason from:
[css-tables-3 §6.1](https://www.w3.org/TR/css-tables-3/#fragmentation) fragments a cell by fitting as
much as it can in this fragmentainer, so **a cell that continues elsewhere has no leftover room in
this fragment to align within.** Vertical alignment is about spare space; a fragment that overflows
has none.

The same shape is waiting for any other caller that measures a box mid-fragmentation. The general
rule this is an instance of lives in
[fragmentation-a-boxs-own-measurements-are-only-valid-at-specific-times.md](fragmentation-a-boxs-own-measurements-are-only-valid-at-specific-times.md);
what this file adds is that the table engine reads exactly this value, twice, and that the failure it
produces is silent — content simply appears on no page rather than anything throwing.
