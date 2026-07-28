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
across the middle of a row.

**What is left of this once #493 is fixed, measured while scoping that work.** #493 was the same shape
read from the other end of the band, and closing it took the mid-cell continuation with it: a pass that
stops now closes the band it leaves, and the pass that resumes opens the next one with the header. So the
remaining surface here is a band a table covers that **no pass either fills or leaves** — one crossed by a
row that is monolithic and overflows, which is exactly the `paged_media_table_tall_row` fixture.

That matters, because it is the one shape where §6.2's "leave room" is **unsatisfiable**. The fixture's
row is a single 620pt block on a ~260pt band; it is drawn once, at one position, and cannot be made to
restart below a header on each band it covers — §2's overflow-rather-than-slice is what puts it there. So
repeating the header on the band it overflows through means drawing the header **on top of the block**,
which is the defect [#439](https://github.com/jhaygood86/PeachPDF/issues/439) was filed for and PR #495
removed. Closing this issue as filed trades a missing header for content drawn underneath an opaque box —
a straight swap of one defect for the other, not a fix — so it wants a decision about which is worse
before it wants an implementation. `<tfoot>` on such a band has the identical problem at the foot.
