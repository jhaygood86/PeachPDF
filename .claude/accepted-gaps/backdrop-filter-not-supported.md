# `backdrop-filter` has no PDF-legal backdrop-sampling mechanism

CSS `backdrop-filter` (any function) needs to read back content already painted *behind* the filtered
element — the backdrop — and filter that. This is a different, more fundamental limitation than the
one covering `filter`'s own cross-channel functions (see
[css-filter-cross-channel-functions-not-representable.md](css-filter-cross-channel-functions-not-representable.md)):
there, PDF has a native per-channel construct (`ExtGState /TR`) that just doesn't extend to
cross-channel math. Here, there is no PDF-legal way to *sample* already-painted content at all — PDF's
transparency model lets a group's content be *composited onto* its backdrop (`ExtGState` knockout/
isolation, blend modes), never *read back* as an input to a further operation. PeachPDF's paint
pipeline is also single-pass and forward-only (`Html/Core/Paint/FragmentPainter` paints each fragment
once, in document order, from already-resolved layout data — see the fragment-tree architecture note
in `CLAUDE.md`), so even a hypothetical PDF mechanism for backdrop read-back would have nothing on the
producing side to feed it: nothing tracks "what has been painted behind this point on the page" as a
queryable value.

This is unrelated to whether a given `backdrop-filter` function is itself channel-independent —
even `backdrop-filter: brightness()` or `backdrop-filter: opacity()`, which would need no cross-channel
math, are blocked by this same backdrop-sampling gap.

`backdrop-filter` stays a documented no-op — see
[HTML & CSS Support](../../docs/html-css-support.md#unsupported-css-features).

Tracked as [issue #1030](https://github.com/jhaygood86/PeachPDF/issues/1030).
