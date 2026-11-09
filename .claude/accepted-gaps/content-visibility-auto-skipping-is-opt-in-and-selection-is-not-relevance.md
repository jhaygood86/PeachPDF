# `content-visibility: auto` skips off-screen contents only for a host that asks, and selection is not relevance

`content-visibility: auto` skips what is not relevant to the user in a `Screen` document that sets `HtmlDocumentOptions.SkipOffscreenContent` (`HtmlDocument.SettleContentVisibilityAsync`: the skipped contents are left out when the tree is built, by source ordinal, and the document is reparsed and laid out again until the relevant set stops changing, at most four times). What is left:

- **Opt-in.** Off by default, and a host has to call `UpdateContentVisibilityAsync` when `ContentVisibilityNeedsUpdate` says the viewport moved. `PeachBrackets.WinUI`, `.Android` and `.Apple` do not call it (the Android and Apple views cannot be built or run here), so they show every element. The spec lets the UA decide what is relevant, and a paged document, which has no viewport, shows everything, so this is a choice rather than a deviation; wiring the views is the remaining work.
- **Selection does not make an element relevant** (§4: the contents of the element are selected). Selection is a range of text runs, not boxes, and skipped contents are not text runs, so a reparse drops a selection that crossed one.
- **Relevance is the box with half a viewport's height around it, or focus** (focus within the element, by source ordinal). The top layer does not count, and the UA margin is not configurable.
- **No `contentvisibilityautostatechange` event, no find-in-page, no focus into skipped contents**: there is no script and no find. Skipped contents cannot be focused.
- **The last remembered size** (`contain-intrinsic-size: auto`) lives as long as the document object; a document loaded again has none.
- `hidden=until-found` still maps to nothing, and `::details-content` still cannot be styled (see [details-content-pseudo-and-content-visibility.md](details-content-pseudo-and-content-visibility.md)).

Tracking issue: [#2309](https://github.com/jhaygood86/PeachPDF/issues/2309).
