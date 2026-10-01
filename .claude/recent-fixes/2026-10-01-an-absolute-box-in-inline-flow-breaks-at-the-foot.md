# An absolute box laid out from inline flow breaks at the page foot (#1537, outside columns)

A page-low `position: absolute` box lost its last line when its containing block's content was only inline flow: for
example `<div style="position:relative"><div style="position:absolute;...">three words</div></div>` low on the page, where
the third line meets the foot. Found by reducing a generated document (seed 260 of the corpus) by delta debugging; about
half of the documents that still lost words on the whole stack reduced to this shape.

**Cause.** A relatively positioned wrapper that holds only an absolute box has inline-only content, so the box is set aside
by `FlowBox` and laid out afterwards by `CssLayoutEngine.LayoutAbsolutelyPositioned`, which **detached the fragmentainer** around
`LayoutBlockChild` ("nothing up this inline flow resumes a record such a box leaves behind"). With the fragmentainer
detached the box was one unbroken run (the word-straddle check is not even asked: `coordinates.Fragmentainer` is null), and
its last line, at y=221 with a 230 foot, was placed across the foot and clipped. The comment predates the box running as
passes of its own (`LayoutBlockChild`'s float/absolute loop, see the #1349 note): that loop resumes its own record, which
is the very thing the detach was avoiding.

**Fix.** Only detach when the current fragmentainer has a band of its own (inside a column) or there is none. Outside a
column the box now runs as its own passes, so the third line goes to the next page.

**Not done.** Inside a column the box still cannot be fragmented against the page: its containing block's `Location.X` is
the last column's, which is the part of #1537 that is still open (see the comment on that issue).

Evidence: `AbsoluteBoxInInlineFlowTests` (the reduction fails before: the last word is on no page; a box that fits is
unchanged); full net8.0 suite passes. On 300 generated documents, on top of the rest of the stack: none worse, 7 better.
