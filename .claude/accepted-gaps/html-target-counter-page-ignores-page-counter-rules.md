# HTML: `target-counter(..., page)` does not see `@page` counter rules

`counter(page)` follows the page counter sequence built from `@page` `counter-reset`/`counter-set`/`counter-increment` (`HtmlContainerInt.BuildPageCounterValues`,
built once pagination is known in `LayoutMarginBoxes`). `target-counter(<link>, page)` is resolved earlier, inside the target-page convergence loop of layout,
before that sequence exists, so it numbers by position (page index + 1 + `PageNumberOffset`) and a document that restarts the page counter gets the wrong number in
a table-of-contents entry that points past the restart.

**Why it is left:** the sequence depends on which `@page` rule each page matches, which depends on pagination, which is what the loop is still settling;
making the two agree needs the sequence inside the loop (and a convergence check on it), a larger change than the counter itself.

**To close it:** compute the sequence from the slot-to-page map on every pass of the convergence loop, and read it in `CssContentEngine.AppendTargetCounter` through
`PageCounterFor`. Delete this file and the sentence in `docs/html-css-support.md` ("`target-counter(<link>, page)` still numbers by position") when done.
Tracking issue: #2433.
