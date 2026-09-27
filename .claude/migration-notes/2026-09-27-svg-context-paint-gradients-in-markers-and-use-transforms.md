# SVG `context-fill`/`context-stroke`: several previously-inert or wrong cases now paint correctly

**A gradient or pattern context paint inside a marker now paints, instead of nothing.** Before, when the shape a
marker was drawn on had a gradient or pattern fill/stroke, `context-fill`/`context-stroke` on the marker's own content
painted nothing at all (silently treated as `none`) - only a solid color came through. It now paints the same
gradient/pattern, correctly positioned in the marker's own (rotated/scaled/translated) coordinate space, as if the
marker's placement were undone.

**A gradient or pattern reaching a shape through `<use>`, with a `transform` between what the `<use>` instantiates and
the shape that actually paints with it, is now measured correctly.** Before, the gradient/pattern's box was computed
as if that intervening `transform` didn't exist, giving a wrong (often visibly shifted or wrongly-scaled) result
whenever such a transform was present; a document with no such transform was already correct and is unaffected.

**`<text>` inside a marker (including its own `text-decoration`), and a filter's `FillPaint`/`StrokePaint` input on a
marker's shape, now resolve `context-fill`/`context-stroke` instead of always resolving to no paint (or, for a
decoration, black).** Before, marker text with a context-paint keyword painted nothing (and, if stroked, silently
took the outlined-glyph paint path instead of the plain text path); its underline/overline/line-through fell back to
black instead of the resolved color; a filter reading `FillPaint`/`StrokePaint` on such a shape got a transparent
input. All now resolve to the shape's actual fill/stroke, same as every other context-fill/context-stroke consumer.

**A gradient or pattern context paint reaching a `<use>` target that has its own `opacity`/`mask`, or a shape filled
through a `<pattern>`, is now measured against the correct box.** Before, content painted through one of these
isolated tiles (`RenderContainerOpacityGroup`/`RenderMaskedElementContent`/a pattern's own tile) measured its
context-fill/context-stroke gradient/pattern against the wrong coordinate-space baseline, giving a wrong (often
drastically so - the wrong half of the gradient, or an unrelated position) result; a document with no such tile in
the way was already correct and is unaffected.

All four are narrow: they only change output for documents that actually combine `context-fill`/`context-stroke`
with a gradient/pattern paint server (markers, `<use>` with an internal transform, or a `<use>`/shape that paints
through an opacity/mask/pattern tile) or with marker `<text>`/its decoration/filter inputs - a document using only
solid-color context paint, which was already correct, renders unchanged.
