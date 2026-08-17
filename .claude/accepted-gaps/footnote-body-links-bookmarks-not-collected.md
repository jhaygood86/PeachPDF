# Links/bookmarks inside a footnote body are not collected into the PDF outline/annotations

`HtmlContainer.GetLinks`/`DomUtils.GetAllLinkAndBookmarkBoxes` walk the live document tree from `Root`
downward. A footnote's body is fully detached from that tree (`DomParser.DetachFootnoteBodies` sets its
`ParentBox` to `null`) and is reachable afterward only through `HtmlContainerInt.FootnoteCalls`/the
fragment tree's `FootnoteAreaFragment`, neither of which this walk consults. A link (`<a href>`) or a
bookmark-candidate heading (`bookmark-level != none`) inside a footnote body is therefore never collected:
it renders correctly (real, laid-out content, same as everything else in the note area) but produces no
clickable link annotation and contributes no outline entry.

This is the same limitation already recorded for `position: running()` content in
[bookmark-outline-running-element-content-not-collected.md](bookmark-outline-running-element-content-not-collected.md)
- both mechanisms detach their content from the tree `GetAllLinkAndBookmarkBoxes` walks, for the same
underlying reason (the content is shown at a page-computed position, not a single canonical one). Filed as
[issue #755](https://github.com/jhaygood86/PeachPDF/issues/755).
