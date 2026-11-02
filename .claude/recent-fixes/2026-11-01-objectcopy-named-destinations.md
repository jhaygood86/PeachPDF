# ObjectCopy: links that use a named destination follow the name's new home

`PdfObjectCopier` copies an annotation's `/Dest (name)` / `/A /D (name)` as written; two merged files that both define `intro`
sent the second file's links to the first file's. `PdfOperationWriter.AssignDestinationNames` now settles every source's names
(first file keeps `intro`, the second becomes `intro_2`, a destination whose page was not taken gets none) *before* any page is
filled, and the copier is given a `Func<string,string?>` that rewrites a link's name or removes the link when it has none.
The `Redraw` path uses the same map, so both modes name destinations identically. The names must be decided before filling
because the copier runs per page, while the old allocation happened afterwards in `CarryNavigation`.

Evidence: `PdfObjectCopyTests` (two files with `intro` -> each file's link lands on its own page; page not taken -> link gone).
