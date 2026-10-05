# Replaced elements now draw their outline

Before: `outline` was ignored on `<img>`, `<object>`/`<video>`, inline `<svg>`, `<iframe>`, `<math>` and form
controls. A stylesheet that set an outline on them (a focus ring, a debug outline) rendered nothing.

After: the ring is drawn around the element's border box, honouring `outline-style`, `-width`, `-color` and
`-offset`, painted after the element's own content like every other outline.
