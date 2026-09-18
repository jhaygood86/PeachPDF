# A path test double that flattens subpaths cannot see topology

`TestGraphicsPath` originally accumulated every `Start`/`AddMove`/`LineTo`/`ArcTo` into one flat
point list. That makes a `DrawPathCall` **unable to distinguish one connected shape from three
disjoint ones** — the point set is identical in aggregate, and the call count is 1 either way.

This cost real debugging time on the wrapped-inline outline union: three tests failed in a way that
read as an algorithm defect, and the geometry had been correct the whole time. The assertions were
simply blind to the only property under test.

## The rule

A test asserting on **shape topology** — "one connected shape", "these stayed separate", "this is a
ring not a blob" — must assert through `DrawPathCall.SubpathStarts` / `Subpath(i)` /
`SubpathBounds(i)`, never through `Points` or the number of `DrawPath` calls.

`TestGraphicsPath` now records a subpath index on each `Start`/`AddMove`, and `AddPath` rebases the
appended path's starts onto its own. Any new path-producing primitive added to the adapter layer has
to keep that recording intact, or this blindness comes straight back.

## The counting rule that goes with it

In an outline band, **each contour contributes exactly 2 subpaths** — the contour itself plus its
inset copy, wound oppositely so the interior cancels under nonzero fill. So one connected shape is 2
subpaths and three disjoint shapes are 6. A test expecting `N` shapes asserts `2 * N` subpaths; if
that ever stops holding, `OutlineRegionPainter.FillBand` has changed shape and the band's fill rule
needs re-checking at the same time.
