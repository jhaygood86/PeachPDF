# A PdfGenerateConfig with no page size renders on US Letter

Before: `new PdfGenerateConfig()` left `PageSize` at `Undefined` and the manual size at 0 x 0, so a document without an `@page` size rendered a page with no content area (a blank page, no error).

Now: with no `PageSize` and no positive `ManualPageWidth`/`ManualPageHeight`, pages are US Letter (612 x 792pt, swapped for landscape). `@page { size }`, an explicit `PageSize` and a manual size still win.
