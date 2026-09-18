# A reused `PdfGenerator` no longer inherits the previous render's font scale

**Before (v0.9.18 and earlier):** a `PdfGenerator` used for more than one render kept a font cache keyed by size alone.
After a render that rescaled the page (`PdfGenerateConfig.ShrinkToFit` or `ScaleToPageSize` on a document wider than the
page), the next render on the same instance could be handed fonts built at that earlier scale — 10pt text drawn as
9.819pt, for instance — so its text was a little smaller than declared and could wrap, size and paginate differently from
the same document rendered on a fresh generator. Output depended on what had been rendered before it.

**Now:** the font caches are keyed by the render's `PixelsPerPoint` as well, so a document renders identically whether the
generator is fresh or has rendered other documents first. Anyone who compared PDFs from a long-lived generator against
PDFs from a fresh one (or reordered a batch and saw sizes change) will see the reused-generator output move to match the
fresh one. Nothing to change in calling code.

Confirmed against v0.9.18: `git show v0.9.18:src/PeachPDF/Html/Core/Handlers/FontsHandler.cs` keys `_fontsCache` by
`double size` only, and `PdfGenerator.GeneratePdf` resets `PixelsPerPoint` without clearing it on a non-rescaling render.
