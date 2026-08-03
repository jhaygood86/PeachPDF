# `@container` size queries don't re-resolve per page under per-page `@page` width overrides

PeachPDF supports per-page `@page :left`/`:right`/named-page margin overrides that vary a page's own
content-box width, re-flowing main-column block content to each page's own measure
(`HtmlContainerInt.UseVariablePageWidth` — see "Per-page content bands" in `docs/architecture.md`).

The `@container` size-query convergence loop (`HtmlContainerInt.PerformLayout`) runs independently of
that per-page reflow and resolves each size query container's size **once per whole-document layout
pass**, not per page. If a size query container's own width happens to vary across the pages it spans
because of `UseVariablePageWidth`'s per-page reflow, the container-query loop does not re-evaluate
`@container` conditions against each page's own resolved width — it uses whichever width that
container resolved to on the pass that recorded it.

This is a narrow, compounding-features gap (`@page` per-page margins × `@container` size queries), not
expected to matter for the common case (an explicit-width size container, or one that never spans a
page boundary with varying margins). Tracked as
[#617](https://github.com/jhaygood86/PeachPDF/issues/617).
