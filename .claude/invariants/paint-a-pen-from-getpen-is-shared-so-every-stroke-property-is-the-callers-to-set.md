# A pen from `GetPen(RColor)` is shared, so every stroke property is the caller's to set

`RAdapter.GetPen(RColor)` returns a pen cached **per color**, so two completely unrelated strokes in
the same color get the same `RPen` object. The cache exists to avoid an allocation per stroke, not to
carry state between callers.

`GetPen` therefore resets the pen to a freshly-created one's settings — width 1, miter limit 0, butt
cap, miter join, solid dash — before handing it back. **Keep that reset, and keep it covering every
property `RPen` exposes.** Adding a new stroke property to `RPen` without adding it to the reset
reintroduces exactly the leak the reset exists to stop.

The measured symptom, and why this is not hypothetical: a dotted border sets a round line cap and a
`[0, period]` dash array, because a dot in PDF *is* a zero-length dash under a round cap. Several
call sites set only `pen.Width` and nothing else — `MarkerFragmentPainter` (the hollow ring for
`list-style-type: circle`, which does not even set width), `FormFieldChrome` (the checkbox tick and
the field border path), `FormFieldAppearanceBuilder` (a comb field's separators). Without the reset,
a document with a black dotted border and a black `circle` list marker paints the marker's ring as a
handful of dots instead of a ring, and the defect only appears when the two happen to share a color.

`GetPen(RBrush)` has no such hazard — it creates a fresh pen every call, deliberately, because
brushes are not identity-stable the way colors are.

The complement still holds on the caller's side: **do not assume anything survives a `GetPen` call.**
Configure every property the stroke depends on, right after retrieving the pen and before drawing.
