# `content-visibility: auto` skipping: what is still left

`content-visibility: auto` skips what is not relevant to the user in a `Screen` document that sets `HtmlDocumentOptions.SkipOffscreenContent` (`HtmlDocument.SettleContentVisibilityAsync`: the skipped contents are left out when the tree is built, by source ordinal, and the document is reparsed and laid out again until the relevant set stops changing, at most four times). The WinUI, Android and Apple views turn it on and call `UpdateContentVisibilityAsync` as the viewport moves, moving the view to `HtmlDocument.Viewport` so content above it changing size does not move what is on screen. An element holding selected text or the focus is relevant, and a selection and the highlights are carried over the reparse by the element each text run belongs to. What is left:

- **Opt-in for a host that embeds the document itself.** `SkipOffscreenContent` is off by default and a host has to call `UpdateContentVisibilityAsync` when `ContentVisibilityNeedsUpdate` says the viewport moved. The spec lets the UA decide what is relevant, and a paged document, which has no viewport, shows everything, so this is a choice rather than a deviation.
- **Relevance is the box with half a viewport's height around it, or the focus, or selected text.** The top layer does not count, and the UA margin is not configurable. A selection that crosses an element whose contents were skipped (the end is in text that has no run) is dropped, not clipped to what is shown.
- **The Android and Apple views are written and compiled (Android) or reviewed (Apple, which cannot be built on Windows) but not exercised on a device**; the WinUI view has a UI test.
- **No `contentvisibilityautostatechange` event, no find-in-page, no focus into skipped contents**: there is no script and no find. Skipped contents cannot be focused.
- **The last remembered size** (`contain-intrinsic-size: auto`) lives as long as the document object; a document loaded again has none.
- `hidden=until-found` still maps to nothing, and `::details-content` still cannot be styled (see [details-content-pseudo-and-content-visibility.md](details-content-pseudo-and-content-visibility.md)).

Tracking issue: [#2309](https://github.com/jhaygood86/PeachPDF/issues/2309).
