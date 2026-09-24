# Page floats (`float: top/bottom/top-bottom/snap`) are not column-scoped

`HtmlContainerInt.ResolvePageFloatsForThisAttempt` resolves every page float's landing slot purely
against the page (`PageIndexOf`/`PageTopOf`/`PageBottomOf`), regardless of the float's own
`float-reference` value. A `float-reference: column` page float inside a `column-count`/
`column-width` container should instead reserve space at the top/bottom of the *column* its anchor
lands in — the way `float: footnote` already does for `float-reference: column`
(`HtmlContainerInt.ColumnAreaFor`/`ColumnFragmentainerRecord`/`FootnoteAreaHeightsByColumn`) — but
today it is always treated as page-scoped instead.

A page float placed directly as a child of a multi-column container is laid out (it used to be dropped
with all-zero geometry, [#1203](https://github.com/jhaygood86/PeachPDF/issues/1203)), but against the page,
not the column - the same gap this file is about.

This mirrors `float: footnote`'s own history: column-scoped note areas were a dedicated follow-up
after page-level footnotes shipped, not part of the original feature. Page floats take the same path
— page-level `top`/`bottom`/`top-bottom`/`snap`/`inside`/`outside` ship first (issue #699), and
column-scoping is deliberately left for a follow-up.

Tracked as [#1272](https://github.com/jhaygood86/PeachPDF/issues/1272).
