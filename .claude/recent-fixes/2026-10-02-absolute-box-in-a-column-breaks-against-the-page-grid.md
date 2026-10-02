# An absolute box in a column breaks against the page grid like any other (seed 214, #1537)

`CssLayoutEngine.LayoutAbsolutelyPositioned` laid an absolute box out with the fragmentainer detached whenever the
current one was a column (`detach = CurrentFragmentainer is not { HasOwnBand: false }`), and `CssBox.LayoutBlockChild`'s
own-pass loop, which breaks such a box at the page foot and resumes it page by page, only ran for a page-level
fragmentainer (`HasOwnBand: false`). #1557 had done this for a box outside a column and recorded that inside one the box
"cannot be fragmented against the page (its containing block is placed per column)". So in `columns: N` > a positioned
div > an absolute box, the box was laid out whole. A line that crossed the page foot was left straddling it, claimed by
the first page and clipped there, with no continuation on the next: `w214_29` (y=228 to 240 on a page whose content
ends at 230) was in the fragment tree and in no pixel. Without the `columns` wrapper the same box broke correctly.

The premise did not hold: css-multicol-1 §2 makes the multi-column container the containing block for an absolute
descendant that has no positioned ancestor of its own inside it, and the box's offsets are against the positioned ancestor
wherever that sits, so the page grid is the right thing to break against. Both gates now let an absolute box through
whatever the current fragmentainer is (a float still needs a page-level one: it stays in its column), and the box is only
detached when there is no fragmentainer at all, which is a measurement pass.

Found by: reducing seed 214 (`columns:2` > `position:relative` > `position:absolute; top:64pt`, 13 one-word lines), then
tracing `LayoutBlockChild` for the box: in the real column fill it arrived with the fragmentainer `none`.

Consequence to know: the box is now positioned against its own column, not the first one. In seed 27 the relative div is in the
third column (x=199), so its box (`left:79pt`, 108pt wide) sits at x=278 and its second word at x=321, past the 300pt page.
Before, the whole-layout path left it at the first column's x and the words happened to be on the page; that was where they
were put by the measurement pass, not where the document asks for them. The corpus counts them as lost now (`27`: 0 to 3)
and they are overflow past the page edge, not content dropped by layout.

Evidence: the issue's own document draws all 80 words (PDF text extraction, two pages); corpus (300 documents) seed 214 goes to 0
and 27 shows the three off-page words; the new test fails on main; net8.0 suite green.