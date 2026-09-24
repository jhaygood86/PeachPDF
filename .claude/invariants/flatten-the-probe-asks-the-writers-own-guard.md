# Which content needs flattening is answered by PdfATransparencyGuard itself, never by a second list

`TransparencyPolicy.Flatten` must flatten exactly what the guard would reject. A predicate written in the painter ("has opacity, rgba, an
alpha image...") would drift the first time a construct is added to the guard (it already covers ~14 call sites in the writer). Instead a
box's own paint is tried on a scratch PDF page with `TransparencyProbe.Observe()` making the guard *record and return* rather than throw.

**Traps:** the scratch document carries no conformance level, so colour or glyph rules of PDF/A/X cannot end a probe before it has seen the
transparency; and any other exception during a probe is swallowed as "inconclusive", because the real paint will hit and report it. The
scratch page is replaced every 200 probes so a large document does not accumulate a second copy of its own content stream.

A bitmap whose pixels are all opaque is embedded without an alpha plane (`RasterEmbedder.IsOpaque`), so it needs no soft mask and is legal
under PDF/A-1; `GraphicsAdapter.DrawRaster` only runs the guard for a surface that is *not* opaque. That is why a `backdrop-filter` (over
white paper) is legal there with no policy at all, and why a flattened region must be built over the white paper background.
