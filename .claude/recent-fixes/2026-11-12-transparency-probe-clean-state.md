# TransparencyProbe starts every probe from a clean graphics state (#1918)

`TransparencyProbe` paints into one reused scratch page. The writer only reaches `PdfATransparencyGuard` when it realizes a *new* fill/stroke color or alpha (`PdfGraphicsState.RealizeFillColor`/`RealizePen`), so a probe repeating the previous probe's translucent fill hit the dedup, never reached the guard, and answered "no transparency". Under PDF/A-1 + `TransparencyPolicy.Flatten` the second identical `rgba()` box was not flattened and the real paint threw.

Fix: each probe runs between `Save()` and `Restore(token)` on the scratch graphics, so the realized-color cache is back to its pristine state. If Restore throws, the scratch page is dropped and the next probe rebuilds it.

Trap: do not "fix" this by resetting more often than every 200 probes only; any reuse of the page is affected, so the state must be reset per probe.

Evidence: `TransparencyFlatteningTests.PdfA1_WithFlatten_FlattensARepeatedTranslucentPaint` (fill and stroke variants) fails without the change and passes with it; PeachPDF.Tests and PeachDrawing.PDF.Tests pass in Release.
