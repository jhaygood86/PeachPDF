# `float: footnote` works on a block-level element

**Before:** `float: footnote` on a block-level element (`<div>`, `<p>`, `<li>`...) did nothing: the element stayed
in the flow, unnumbered, as if it were `float: none`. Only inline sources (`<sup>`, `<span>`...) were footnotes.

**Now:** a block-level element is a footnote source like any other. It is replaced in the flow by the numbered call
(inline), which gets an anonymous block of its own between block siblings or joins the run of text beside it, and
its content moves to the page's note area. A source that is absolutely positioned, `position: running()`,
`display: none` or a table-internal display is still not a footnote.

**Also:** a footnote inside a repeated table `<thead>`/`<tfoot>` is now left as ordinary content rather than being
attached to whichever page laid the header out last.
