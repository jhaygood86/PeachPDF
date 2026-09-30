# SVG `context-fill` and `context-stroke` are supported

**Before:** in an inline `<svg>` or an SVG image, `fill="context-fill"` (or `stroke`, or the same in a `style` attribute or a stylesheet rule) was an
unrecognised paint: the declaration was ignored and the element kept the fill or stroke it inherited (black fill, no stroke by default). Only the
glyph documents of an OpenType SVG font had the keywords, rewritten to the text colour.

**Now:** the keywords are the fill and stroke of the context element (SVG 2): the `<use>` an element is instantiated by, or the shape a marker is
drawn on. Outside both there is no context element and they paint nothing, so a bare `<rect fill="context-fill">` disappears instead of being
black. In the glyph document of an OpenType SVG font `context-fill` is still the text colour, and `context-stroke` is now no paint (it used to be
the text colour too).
