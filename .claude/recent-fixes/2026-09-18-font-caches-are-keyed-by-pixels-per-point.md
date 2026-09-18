# The font caches are keyed by `PixelsPerPoint` as well as size

## The bug

`FontsHandler` cached fonts by `(family, size, style, weight, stretch, oblique)`. The `size` a caller passes is in
**layout units** (points × `PixelsPerPoint`), and `PdfSharpAdapter.CreateFontInt` builds the font behind it at
`size / PixelsPerPoint` points. So the same numeric size is a *different physical font* under a different scale, and
the cache could not tell them apart.

A `PdfGenerator` is reused across sequential renders, and a `ShrinkToFit`/`ScaleToPageSize` render changes
`PixelsPerPoint` (and clears the cache only on the render that rescales). The next render then asked for
`size = 10` at scale 1 and was handed the font a previous render had built for `size = 10` at scale 1.0184: a 9.819pt
font. Measured in the PDF's `Tf` operators: 10pt text at 9.819, 20pt at 19.638, 11pt at 10.801. Everything laid out
after that measured against the wrong font.

## How it was found

`css_grid_intrinsic`'s four spanned-row items read 81.0pt at v0.9.18, 79.5pt at the commits after it, and 69.3pt from
#1144 onward, while Chrome gives 79.5pt. A bisect blamed #1144, which was not the cause: rendered on its own with the
CLI (a fresh `PdfGenerator`) the showcase is 79.5pt at the tip. The TestHarness reuses one generator across every
showcase, so what a showcase gets depends on which `ShrinkToFit` showcase ran before it; #1144 widened
`cascade_layers`' cards, which changed *that* showcase's shrink factor and, through the leaked fonts, the one after
it. A scratch program rendering a wide `ShrinkToFit` page and then `css_grid_intrinsic` on one generator reproduced
Tf sizes of 8.144 / 8.959 / 16.289 and a 56.2pt item; a fresh generator gave 79.5pt.

## The fix

`RAdapter.FontSizeScale` (1 by default, `PdfSharpAdapter.PixelsPerPoint` for the PDF adapter) is part of every font
cache key: the size level of `_fontsCache` is `(size, scale)`, and the per-codepoint and system-fallback caches carry
the scale in their tuple keys. The same (size, scale) is still one instance, so the cache keeps doing its job for a
generator that keeps rendering at one scale.

## Deliberately not done

- **Not "clear the cache whenever `PixelsPerPoint` is reset".** That is the other obvious fix; it would throw away
  every resolved font at the start of each render of a reused generator, which is exactly the reuse the cache exists
  for. Keying by scale keeps a generator that alternates scales warm for both.
- **The clear at the rescale in `PdfGenerator.GeneratePdf` stays.** It is no longer needed for correctness, but it is
  what bounds the cache: `ShrinkToFit` produces a fresh, continuous scale per document, and without the clear a
  long-lived generator would gain a set of entries per render.

## Evidence

- `FontCacheScaleTests` (6): the same size at two scales is two fonts of the right physical size (10pt and 8pt at
  1.0 and 1.25); returning to a scale finds its own cached instance again; the mapped-family write path, the
  per-codepoint cache and the system-fallback cache are each keyed by scale; `ClearFontCache` still drops every
  scale. With `FontSizeScale` neutralised to `1.0`, 4 of the 6 fail.
- Related suites (Font, Adapters, ShrinkToFit, PixelsPerPoint, Rescale, PdfGenerator): 1125 passed.
- TestHarness on this branch (together with the block-height fix in the same PR): `css_grid_intrinsic` items 79.5pt
  (Chrome 79.5), `css_grid` cells 31.2 / cards 60 / spanned 70.5 (Chrome 31.5 / 60 / 70.5), `css_grid_subgrid`
  27.2 / 38.5 / 72.2 (Chrome 27.0 / 38.2 / 72.0).

## A trap for whoever compares harness output between releases

The TestHarness renders every showcase with one shared `PdfGenerator`, so before this fix its output for a showcase
depended on the *order* of the showcases and on their shrink factors. A before/after comparison of two harness runs is
only clean if the generator is fresh per showcase (or both builds carry this fix).
