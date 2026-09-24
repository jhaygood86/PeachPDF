# `float: footnote` inside a repeated table header or footer is left as ordinary content (#750)

A table `<thead>`/`<tfoot>` that repeats across pages is laid out once and translated onto each page
(`CssProxyBox`, which re-translates one source subtree per proxy). A footnote call inside one therefore has
no single page of its own: `HtmlContainerInt.ResolveFootnotesForThisAttempt` reads a call's page from its
geometry, which reflects whichever proxy laid out last, so the note would attach to the header's last repeat
page while every page shows the same call. `DomParser.IsFootnoteSource` declines such a source instead, so it
renders as the ordinary inline content it would be without `float: footnote`.

[css-tables-3 §6.2](https://www.w3.org/TR/css-tables-3/#repeated-headers) repeats a header as a rendering copy
and says nothing about footnotes in it; the most defensible reading is one call and one body from the
original position, none in the repeats. That needs a per-page (call, slot) instance model - capturing the body
once and sharing it across the repeats the way `LayoutMarginBoxes` captures a running element - which is a
redesign of how a footnote is tied to a page, not a fix.

A footnote inside a table cell, or inside a flex or grid item, needs no special handling: `DetachFootnoteBodies`
runs over the whole tree before any layout engine starts, so the call is ordinary content of the cell or item
(`FootnoteIntegrationTests.Footnote_InsideATableCellOrFlexOrGridItem_ReservesANoteAreaOnItsPage`).

Tracked as [#750](https://github.com/jhaygood86/PeachPDF/issues/750), narrowed to this case.

Mirrors the caution in [running-element-inside-table-cells.md](running-element-inside-table-cells.md), a
different mechanism with the same "per-cell bookkeeping is more invasive to audit than the generic walk"
caution.
