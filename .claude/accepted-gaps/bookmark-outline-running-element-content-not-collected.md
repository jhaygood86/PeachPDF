# PDF bookmarks: headings inside repeated running/margin-box content or repeated table headers/footers are not collected

`BookmarkOutlineBuilder` collects bookmark candidates (`bookmark-level != none`) by walking only the
main document box tree (`DomUtils.GetAllLinkAndBookmarkBoxes`), mirroring the existing `GetAllLinkBoxes`
walk `PdfGenerator.HandleLinks` already used for `<a>` links. An element that only ever appears as a
GCPM running element inside an `@page` margin box — repeated per page, with no single canonical document
position — is never visited by this walk, so it never generates a bookmark even with a non-`none`
`bookmark-level`. The same applies to a `<thead>`/`<tfoot>` repeated across page breaks via `CssProxyBox`
(its repeated-header/footer subtree isn't reachable through `CssBox.Boxes`, the same walk `<a>` links
already don't reach either) — a reader will likely hit this case sooner than the margin-box one. See
[PDF Bookmarks (Outline) Support](docs/html-css-support.md#pdf-bookmarks-outline-support).
Tracked in [#714](https://github.com/jhaygood86/PeachPDF/issues/714).
