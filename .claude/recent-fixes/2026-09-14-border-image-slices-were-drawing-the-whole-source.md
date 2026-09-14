# border-image drew the whole source into every one of its nine regions

`BorderImageDrawHandler` computes a source rectangle per region and calls
`RGraphics.DrawImage(image, destRect, srcRect)`. The PDF backend threw `srcRect` away:
`XGraphicsPdfRenderer.DrawImage(image, destRect, srcRect, unit)` carried a
`// TODO: incomplete - srcRect not used` and drew the *entire* image into `destRect`. So every corner,
every edge and every tile of a repeated edge painted the whole texture squashed into its own band — a
frame far denser and differently coloured than the author wrote. Found by rendering the 12-page SVG
demo and looking at it, not by a test: the whole `BorderImagePaintIntegrationTests` suite asserted
destination geometry only, and the recording mock did not even record `srcRect` (it does now).

## The crop is a clip, not a renderer feature

PDF has no "draw this sub-rectangle of an XObject" operator, for a raster image or a Form XObject.
The only way to crop is to clip to the destination and place the *whole* image at the scale and offset
that lands `srcRect` exactly on it — `GraphicsAdapter.ComputeCroppedPlacement` plus a `PushClip`.
That is deliberately in `GraphicsAdapter`, not in the PdfSharpCore fork: it needs `RImage.Width`'s
"natural units" contract (device pixels for a raster, points for an `XForm`), which is an adapter-layer
concept. The fork's overload keeps its old whole-image behaviour and now says in a doc comment that it
cannot honour `srcRect` and why, instead of a TODO nobody reads.

Two things that had to be preserved, both found by the suite rather than by reading:

- **A whole-image `srcRect` must emit byte-identical operators.** Every background layer passes
  `(0, 0, image.Width, image.Height)`. Routing that through the plain two-argument `DrawImage` changed
  the emitted stream — the form branch there appends a vestigial `100 Tz` — and broke two background
  tests that pin the `cm`/`Do` sequence. Hence `IsWholeImage` short-circuits to `DrawWhole`, which
  calls the same `XGraphics` overload as before.
- **Nearest-neighbour is now forced for the whole nine-slice paint.** Clipping does not stop a
  smoothing sampler from reading across the cut: it pulls half a source pixel of the neighbouring
  slice in, and at the 6x-and-up scales a slice-to-border stretch reaches, that was a quarter of the
  border width bleeding the centre colour into the ring. `DrawEdge`/`DrawMiddle` used to toggle
  `Interpolate` for tiling seams; the toggle moved up to cover corners and stretched edges too.

## An SVG source is sliced at its own intrinsic size

Second, separate defect in the same picture. `ResolveSourceImage` treated *every* non-raster source as
sizeless and rendered it into a tile the size of the border-image area. For an SVG carrying
`width`/`height` (or a `viewBox`), that stretches the artwork over the box before slicing it — circles
become ellipses, and every edge tile comes out at the wrong aspect. CSS Images 3's default sizing
algorithm uses an intrinsic size verbatim when no size is specified, so the SVG is now rendered at
`intrinsic × Length.PointsPerPx` and only a genuinely sizeless SVG falls back to the area (as a
gradient still does). One consequence worth knowing: the cached Form XObject for such a source is now
keyed at the artwork's own size, so it is shared by every box using it at any border size, not one form
per border-image area.

`border-image-slice`'s bare `<number>` is "vector coordinates" for a vector source — CSS pixels — while
the tile it is cut from is measured in points, so `ResolveSlice` takes a `numberUnit` (1 for a raster,
whose natural size is already in the same device pixels; `Length.PointsPerPx` for anything rendered
into a tile).

## Evidence

- Full suite on net8.0: 11492 passed. The two `LineClampIntegrationTests` failures and the
  allocation-budget `AppendPdfNumber` test pre-date this change (the first two reproduce on a stashed
  tree; the third passes when run alone and only fails under parallel load).
- 100% diff coverage on the library changes (55 measurable lines).
- Rasterized through both PDFium and MuPDF: a synthetic 3x3 colour grid now puts each colour in its own
  region (it previously put all nine into each), the `border_image` showcase renders as an actual
  picture frame, and the gradient frames run continuously around the border instead of repeating the
  whole gradient nine times.

No migration note: `border-image` itself landed unreleased earlier the same day
(see [2026-09-14-border-image-painting-and-its-missing-cascade-wiring.md](2026-09-14-border-image-painting-and-its-missing-cascade-wiring.md)),
so there is no released behaviour for a document author to have seen.
