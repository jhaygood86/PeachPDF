# target-counter()/target-text(): targets inside repeated running/margin-box content or repeated table headers/footers are not resolved

`CssContentEngine`'s `<target>` resolution (`target-counter()`/`target-text()`, `AppendTargetCounter`/
`ResolveTargetText` → `ResolveTarget` → `HtmlContainerInt.GetBoxById`) walks only the main document box
tree (`DomUtils.BuildIdIndex`), the same box-tree walk `DomUtils.GetAllLinkAndBookmarkBoxes` already uses
for PDF bookmarks and `<a>` links. An element that only ever appears as a GCPM running element inside an
`@page` margin box, or as part of a `<thead>`/`<tfoot>` repeated across page breaks via `CssProxyBox`, is
never indexed by this walk, so `target-counter(url(#id), ...)`/`target-text(url(#id))` silently resolve to
nothing (an unresolved target, same as a typo'd id) rather than throwing — no different in kind from the
existing PDF-bookmark limitation, since both features share the identical underlying walk and the identical
fix would close both at once. See
[Generated Content](docs/html-css-support.md#generated-content).
Tracked in [#714](https://github.com/jhaygood86/PeachPDF/issues/714) (the same tracking issue as the
PDF-bookmark case — see
[bookmark-outline-running-element-content-not-collected.md](bookmark-outline-running-element-content-not-collected.md)).
