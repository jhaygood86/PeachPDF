# A repeated header is carried onto a page a break falls on, not onto every page the table spans

`CssLayoutEngineTable` creates a `<thead>`/`<tfoot>` proxy in exactly two places: once before the body-row
loop, and once inside `TakeBreakBeforeRow`. So the group is repeated on the page the table begins on and
on every page a **break** opens — and on no other. A page a table merely *spans*, with no break falling
on it, gets none.

[css-tables-3 §6.2](https://www.w3.org/TR/css-tables-3/#repeated-headers) repeats the group on every page
the table spans. Tracked as [issue #509](https://github.com/jhaygood86/PeachPDF/issues/509), visible in
the `paged_media_table_tall_row` showcase: the header is on page 0 and page 2 and absent from page 1,
which the too-tall first row overflows onto. A single-row table taller than one page is the same shape
with nothing else in it.

**Older than the change that made it easy to see.** Before
[#432](https://github.com/jhaygood86/PeachPDF/issues/432) the row loop took a break after a too-tall row
whose target lay *above* the row — so a header was drawn on that page, over the row's own content, at a
band the content had long passed. Removing the bogus break removed the accidental header with it. Any
table spanning pages without breaking has always had exactly one.

**Why it is not a line in `TakeBreakBeforeRow`.** "A break was taken" is the wrong question; the right
one is which bands the table's slice covers, which is only knowable once the row loop has finished — and
it is the same set `CssBox.PageBreakBottoms` would have to grow entries for so the table's border is
clipped on those pages too. Doing one without the other trades a missing header for a border drawn
across the middle of a row. `<tfoot>` wants the same answer from the other end of the band, which is what
[#493](https://github.com/jhaygood86/PeachPDF/issues/493) is about.
