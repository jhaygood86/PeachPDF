# CFF stem darkening is reachable from the public API

`OutlineRequest.StemDarkening` (default `false`) turns on the stem darkening of the ported Adobe CFF engine for a grid-fitted glyph of a font with CFF
outlines, and `PdfGenerateConfig.TextStemDarkening` (default `false`) passes it from the raster backend, for text that is hinted (`TextHinting` not `None`).
The engine had it since the CFF port (`CffSize`'s `stemDarkening` constructor argument, tested against FreeType with it on) but nothing public reached it,
because FreeType's driver default leaves it off. The two settings are off by default, so no output changes unless they are asked for.

## What the load-bearing idea was

- **The flag is part of both cache keys.** `HintingEngine`'s `SizeKey` (and so `GlyphKey`) now carries it, otherwise a darkened request after a plain one for
  the same glyph and size would be served the plain outline. `TheDarkenedAndThePlainOutlinesAreCachedApart` asks in the order darkened, plain, darkened,
  plain and checks both answers stay apart and are shared (`Assert.Same`).
- **A TrueType font must not split its cache.** Darkening means nothing for it, so `Get` clears the flag for a face that is not CFF, exactly as it already
  forces `Standard` for a CFF face (Adobe's engine has no modes). Asking a TrueType font for darkened outlines returns the very same cached outline object.
- **Why a member of `OutlineRequest`, not another `GridFitting` value.** Darkening combines with any fitting mode (`Standard` and `Monochrome` are one
  behaviour for CFF) and is an independent yes/no; a `GridFitting.StandardDarkened` would double the enum for the one font kind it applies to. The register
  row in `text-public-api-must-not-mirror-a-competitor.md` says so.
- **It is FreeType's default darkening only.** The amount comes from the driver's default parameters (0.4 px total for stems up to half a pixel wide, 0.275
  px for one to 1.667 px, nothing from 2.333 px; the engine halves it per side). `darkening-parameters` is not exposed. `LargeTextIsNotThickenedByStemDarkening`
  pins the "nothing for wide stems" end at 120 ppem, so a caller that leaves the flag on for big text pays nothing.

## What was found by running it

- **The existing golden already had the reference.** `HintingCff.golden.json.gz` has a `darkened` mode (FreeType with `no-stem-darkening` off) at two sizes for
  every font, used by the internal `CffSize` test. What was missing was the public path, so `HintingCffApiGoldenTests` walks the same golden through
  `Typeface.TryGetOutline` with the flag on and off, point by point in 26.6. It passed on the first run for all 38 (font, mode, size) runs; with the flag
  deliberately not forwarded 12 of them fail, so the test does discriminate. The point walk needed care: FreeType's contour close drops a last on-curve point
  that lies on the first, so the API's last cubic segment of such a contour ends at the contour start and consumes two golden points, not three (see the CFF
  hinting entry).
- **The raster showcase is the visual check.** `text_hinting_cff_stem_darkening` in the test harness renders the CFF hinted page with the flag on; the small
  sizes are visibly heavier than in `text_hinting_cff_standard` and the large ones are the same.

## Deliberately not done

The driver's `darkening-parameters` property, and any darkening for TrueType (FreeType has none) or for unhinted outlines. A CLI switch: the CLI has no
`TextHinting` option to hang it on.
