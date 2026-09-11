# Fill/stroke constant-alpha dedup missing an IsEmpty guard

## The load-bearing idea

`PdfGraphicsState.RealizeFillColor` (`src/PeachPDF/PdfSharpCore/Drawing.Pdf/PdfGraphicsState.cs`)
tracks the last-emitted fill color in `_realizedFillColor` to dedup `rg`/`ca` writes to the content
stream. The RGB-change check already guarded against `_realizedFillColor` being `XColor.Empty` (the
field's default, and what `RealizeBrush`'s gradient-pattern branch explicitly resets it to after a
`/Pattern cs` switch) with `_realizedFillColor.IsEmpty || _realizedFillColor.Rgb != color.Rgb`. The
alpha-change check three lines below it did not: `_realizedFillColor.A != color.A` alone. Since
`XColor.Empty.A == 0`, a fill whose own alpha is genuinely `0` right after such a reset evaluated
`0 != 0` as `false` and silently skipped emitting `/ca`, leaving the shape painted at whatever alpha
the enclosing graphics state had (typically fully opaque) instead of invisible. Found via independent
dual-renderer (PDFium + MuPDF) rasterization verification of #997 ("Draw color-glyph artwork once into
a Form XObject") - a separate, pre-existing bug unrelated to that PR's own COLR paint-alpha fix (see
that PR's own merged migration note on COLR paint alpha for the distinct, already-fixed issue it
covers).

Fixed by adding the same `IsEmpty` guard to the alpha check.

## What was found by running it, not by reading it

A post-change review pass (3 finder agents) independently converged on a second, unfixed instance of
the identical defect: `RealizePen`'s stroke-alpha check (same file, `~line 229`) has the exact same
shape - `_realizedStrokeColor.A != strokeAlpha` with no `IsEmpty` guard - even though its own sibling
RGB check a few lines above (`~line 205`) already carries the guard, with a comment explicitly citing
the fill side as precedent. `_realizedStrokeColor` starts at `XColor.Empty` and is reset to it after
any brush/pattern stroke (`~line 247`), so a solid zero-alpha stroke (CSS/SVG `stroke-opacity: 0`) as
the first stroke in a fresh state, or right after a gradient-stroked shape, hit the same silent-opaque
bug on `/CA` instead of `/ca`. Fixed in the same change once confirmed reachable and reproduced by a
regression test (reverting just the stroke guard made the new stroke test fail, confirming it isn't a
false positive).

The regression tests also went through one revision after review: the first version asserted `/ca 0`
as a substring of the whole saved PDF, which - per this repo's own testing convention about
content-stream-substring tests being weak proof for transparency-group correctness - could pass even
if the emitted ExtGState were never actually wired to the draw via `gs`. Replaced with a helper
(`AssertConstantAlphaAppliedBeforeOperator`) that finds the `gs` immediately preceding the `f`/`S`
paint operator in the (decompressed) content stream, resolves that resource name to its indirect
`ExtGState` object, and asserts the specific `/ca`/`/CA` key there - proving the ExtGState is both
emitted and actually applied to the draw, not just present somewhere in the file.

## Deliberately not done

Manual dual-renderer (PDFium + MuPDF) visual verification was done ad hoc against a scratch PDF (an
opaque blue background box behind a CSS `opacity` tile containing an SVG rect with `fill-opacity: 0`
as the tile's first-ever fill), confirming both renderers agreed: pre-fix the transparent overlay
washed the blue background to near-white (opaque, wrong color due to an unrelated fill-color-resolution
quirk on zero-alpha SVG fills - not investigated further, out of scope for this fix since it doesn't
affect alpha correctness), post-fix the blue background showed through correctly. That scratch
repro was not committed as a test - the structural content-stream assertions above are the committed
regression coverage, chosen because this bug is about PDF content-stream emission logic itself, not a
paint/layout call-sequence or a genuinely visual compositing feature, so a structural assertion (this
repo's testing conventions call it out as an acceptable alternative to rasterization) is proportionate.

## Evidence

Full net8.0 suite: 10200 passed / 0 failed / 9 skipped (pre-existing platform-specific skips). Diff
coverage 100% (gate is 90%) against `main` on both changed lines. Zero-warning
`dotnet build PeachPDF.slnx -t:Rebuild`. Both regression tests independently confirmed to fail when
their respective `IsEmpty` guard is reverted (fill and stroke checked separately). Manual dual-renderer
(PDFium + MuPDF) rasterization of a scratch repro confirmed the visual defect and its fix agree across
both engines.
