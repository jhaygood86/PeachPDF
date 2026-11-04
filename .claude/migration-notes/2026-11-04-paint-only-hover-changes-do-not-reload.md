# A hover/focus change that only repaints keeps the document, and `DocumentElement` handles, valid

Before (`v0.9.26-preview1`): every change of pointer or focus state that a `:hover`/`:active`/`:focus` rule
read loaded and laid out the document again, so `DocumentElement` handles, text runs and the accessibility
tree taken earlier were stale after it, and `UpdateResult.DirtyRegions` were the changed elements widened by a fixed 24 points.

Now: when the new state only changes paint-only properties (colours, opacity, transform, filter, shadows,
outlines, text decoration, cursor...), the styles are updated in place, no layout runs, handles and runs stay
valid, `UpdateResult.Incremental` is `true`, and `DirtyRegions` cover the old and new painted extent (so a
large shadow or a scale-up is repainted fully and a small change is repainted tighter than 24 points).
Anything else behaves as before. A host that blanked the view while a restyle laid out can skip that when
`Incremental` is true. `HtmlDocumentOptions.IncrementalRestyle = false` or the environment variable
`PEACHPDF_FULL_RESTYLE=1` restores the old behaviour.
