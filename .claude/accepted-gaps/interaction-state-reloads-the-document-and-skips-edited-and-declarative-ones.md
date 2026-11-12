# A change of hover/focus state that can move something still reloads the document

`SetPointerStateAsync`/`SetFocusAsync` apply a state that only changes how something is *painted* in place
(`InteractionRestyler`: the boxes that match different rules under the new state are cascaded again with
their descendants and kept only if every changed property is on its paint-only list - colours, opacity,
transform, filter, shadows, outlines, text decoration, cursor, caret/accent colour, mix-blend-mode).
Everything else still takes the old route: the source is parsed and cascaded again with the new state and
laid out (the same wholesale redo the container-query convergence loop uses). That keeps every selector
form and every layout-affecting rule correct, but costs a full pipeline run per change.

What still reloads (each gives `UpdateResult.Incremental == false` and `HtmlDocument.LastRestyleFallbackReason`):

- Any layout- or tree-affecting property in the diff: `display`, `content`, `position`, `float`,
  `visibility`, sizes, padding/margin, fonts, counters, list/table/flex/grid, `container-type`, and anything
  not on the paint-only list (so `hover:font-bold`, `hover:p-*`, `hover:hidden` reload).
- (An inline SVG with a state-reading rule on one of its elements, `a:hover .icon`, is no longer one of these: the graphic keeps the children it was built from
  and is built again in place, `CssBoxSvg.RebuildForState`. It reloaded the whole document before, about 0.6-1.5 s per hover change on the arstechnica.com front page,
  now about 0.1 s.)
- A new state that would add or mark a pseudo-element (`::before`/`::after`/`::marker`/`::first-letter`),
  and any document with a state-reading rule on another pseudo-element (`::first-line`, `::placeholder`,
  `::selection`...): those styles are resolved apart from the box's own and are not redone.
- A transform or filter that appears or disappears on a box with absolutely/fixed-positioned descendants
  (it becomes their containing block).
- A restyle that reaches a table repeat/spacing box, foreign content, or more than a few thousand boxes.
- A form change still waiting for its restyle (`NeedsRestyle`), whose attributes are only in the source
  once it is loaded again.
- `HtmlDocumentOptions.IncrementalRestyle = false`, or `PEACHPDF_FULL_RESTYLE=1`.

Not done at all:

- A document built with the declarative API (`CreateAsync`) has no source to parse again, and a document
  whose text was edited in place would lose its edits; both report `Applied = false` with a reason.
- With `AnimationsEnabled`, the transitions a state change that loads the document again would start are run (the style of each element is kept before and compared after); see
  [the live playback gaps](css-animation-live-playback-limitations.md) for the ones that are not. A document without `AnimationsEnabled` jumps to the new style.
- Nothing is rolled back when the fast path gives up halfway: the tree it leaves is discarded by the
  reload that follows, so it is never read. Do not add a code path that gives up and then keeps the tree.

Measured and rejected: cascading into throwaway shadow boxes and adopting the result. The cascade reads
and writes the live box in dozens of places (siblings, pseudo-element synthesis, `display: contents`
shells), so a faithful shadow would have been a second implementation of it; cascading in place and
reloading on any surprise keeps one.
