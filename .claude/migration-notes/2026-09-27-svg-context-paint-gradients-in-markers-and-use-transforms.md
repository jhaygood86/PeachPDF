# SVG `context-fill`/`context-stroke`: three previously-inert cases now paint

**A gradient or pattern context paint inside a marker now paints, instead of nothing.** Before, when the shape a
marker was drawn on had a gradient or pattern fill/stroke, `context-fill`/`context-stroke` on the marker's own content
painted nothing at all (silently treated as `none`) - only a solid color came through. It now paints the same
gradient/pattern, correctly positioned in the marker's own (rotated/scaled/translated) coordinate space, as if the
marker's placement were undone.

**A gradient or pattern reaching a shape through `<use>`, with a `transform` between what the `<use>` instantiates and
the shape that actually paints with it, is now measured correctly.** Before, the gradient/pattern's box was computed
as if that intervening `transform` didn't exist, giving a wrong (often visibly shifted or wrongly-scaled) result
whenever such a transform was present; a document with no such transform was already correct and is unaffected.

**`<text>` inside a marker, and a filter's `FillPaint`/`StrokePaint` input on a marker's shape, now resolve
`context-fill`/`context-stroke` instead of always resolving to no paint.** Before, marker text with a context-paint
keyword painted nothing (and, if stroked, silently took the outlined-glyph paint path instead of the plain text path);
a filter reading `FillPaint`/`StrokePaint` on such a shape got a transparent input. Both now resolve to the shape's
actual fill/stroke, same as every other context-fill/context-stroke consumer.

All three are narrow: they only change output for documents that actually combine `context-fill`/`context-stroke`
with a gradient/pattern paint server (markers, or `<use>` with an internal transform) or with marker `<text>`/filter
inputs - a document using only solid-color context paint, which was already correct, renders unchanged.
