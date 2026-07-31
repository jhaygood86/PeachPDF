# The rows after an oversized row now follow it onto the next page

**Landed:** 2026-07-29 (9d5bb67c) — Repeat a table's &lt;thead&gt;/&lt;tfoot&gt; on every page it spans, slicing the row that overflows (#524)
**Doc section:** docs/html-css-support.md § [Page Breaks](../../docs/html-css-support.md#page-breaks)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

The rows after a row taller than the page used to be placed back inside it — on the page it began on rather than the page it ends on — and drawn over its content. They now follow it, so a document containing such a row gains the pages that row really occupies and everything after it moves down. A repeating `<thead>`/`<tfoot>` is now also drawn on those pages, and the row's own content is pushed down on each of them by the room the group takes — so such a document can gain a further page. See [Forward compatibility](#forward-compatibility).
