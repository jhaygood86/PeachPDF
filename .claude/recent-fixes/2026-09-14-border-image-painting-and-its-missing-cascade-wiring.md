# border-image: the real gap was missing cascade wiring, not just a missing painter

Implements CSS Backgrounds and Borders 3 §13 `border-image` painting: the standard 9-slice algorithm
(4 corners scaled to fit, 4 edges stretched/tiled per `border-image-repeat`, an optional filled
center), for a raster `url()`, an SVG `url()`, or any gradient `<image>` as the source.

## Load-bearing idea: the parsing layer was complete, but nothing carried a resolved value onto `CssBox` at all

The premise going in - "parsing/cascade support is complete, only the painter is missing" - was only
half true. `border-image-source`/`-slice`/`-width`/`-outset`/`-repeat` had full, correct grammar
support at the CSS-OM (`StyleDeclaration`) layer, but `css-properties.json` had zero entries for any
of them and `CssBox` had no fields to hold a resolved value - so even before a painter could exist,
there was nowhere for a declared value to land. Confirmed by grepping `Html/Core/` for `BorderImage`
before starting: zero matches, versus dozens for `background-image`'s equivalent wiring.

Adding five `css-properties.json` entries turned out to be genuinely cheap once the property-registry
generator's own `area` mechanism was understood correctly: grouping all five under the pre-existing,
100%-non-inherited `BorderArea` meant the generator produced `CssBox.BorderImageSource`/`Slice`/
`Width`/`Outset`/`Repeat` automatically - no hand-written field, no `InheritStyle` restore-list entry
needed (unlike `line-clamp`'s own area-membership bug), since an area whose every member is
non-inherited is never whole-adopted from the parent in the first place. `border-image-source` uses a
custom setter into a `CssImage?` (mirroring `background-image`'s own `IReadOnlyList<CssImage>?`
pattern, just singular); the other four are stored as their raw declared strings (mirroring
`background-size`/`background-repeat`), resolved into pixel geometry at paint time by a new
`BorderImageLayerResolver` - the same "share the grammar, defer only runtime-dependent arithmetic"
split `BackgroundLayerResolver` already established for backgrounds.

## A gradient/SVG source has no natural size to slice against

`border-image-slice`'s percentages are relative to the source image's own size, which a raster `url()`
has and a gradient or SVG document does not. Resolved the same way `CssImagePainter.PaintGradientLayer`/
`PaintSvgLayer` already resolve an "auto" `background-size` for a generated image: render it once, via
`RGraphics.CreateTile`, at the size of the border-image area itself (the border box, extended by
`border-image-outset`) - so its own slice percentages are always relative to that area. Reused (made
`internal`, not duplicated) `CssImagePainter`'s own `GetLinearGradientBrush`/`GetRadialGradientBrush`/
`GetConicGradientBrush` helpers to build the brush painted into that tile, rather than a second copy of
the gradient-line/stop-normalization math.

## What was deliberately not done

- `border-image-repeat: round` degrades to `repeat` (plain edge-to-edge tiling relying on a pushed clip
  to cut the final partial tile, not real even-fit resizing) - mirrors this engine's own pre-existing
  `background-repeat: round` simplification, so the two properties don't disagree with each other.
- `border-image-repeat: space` is not a recognized keyword (`BorderRepeat`/`Map.BorderRepeatModes` have
  no entry for it) - a pre-existing CSS-OM gap, not introduced here, but directly relevant since it's
  now clear the paint side would also need real gap-insertion math to honor it.
- `border-image-outset`'s CSS-OM converter accepts a `<percentage>` component, broader than the spec's
  own `<length> | <number>` grammar - a pre-existing, already-tested deviation at the parsing layer,
  left as-is (not silently "fixed") and given a defined resolution (percentage of the same side's own
  border width) rather than being rejected at paint time.

Both round/space gaps are recorded together in one accepted-gap file (issue #1057), since fixing
either means the same real tiling-math work.

## Evidence

72 new/existing border-image tests pass (12 end-to-end paint tests via a `TestRecordingGraphics`
recording the actual `DrawImage` call sequence/geometry - corners, edges, fill, outset, repeat,
rounded-corner clipping, gradient/SVG sources via a tile-capable graphics subclass, and the
fall-back-to-ordinary-border path - plus 27 pure `BorderImageLayerResolver` unit tests and the
pre-existing 33 CSS-OM parser tests). Full suite: 11450/11450 (net8.0), 0 failures. Diff coverage:
100%. Full solution rebuild: 0 warnings. The `border_image` showcase was rasterized through both
PDFium and MuPDF; both agree, including the gradient-source and rounded-corner cases.
