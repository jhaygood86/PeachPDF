# COLR path serialization avoids boxing and transient point arrays

_Landed 2026-09-10._

Profiling searchable COLR/CPAL rendering showed that shaping was no longer material, but emitting the
vector outlines still boxed every coordinate through `StringBuilder.AppendFormat`. Building each glyph
path also repeatedly grew its point/type lists and then copied both lists to arrays solely so the PDF
renderer could enumerate them.

PDF coordinates now use `double.TryFormat` into a stack buffer and append that span directly to the
content builder. The uncommon value whose fixed-point representation exceeds the buffer falls back to
the same invariant `double.ToString` formatting, so large finite values retain the old behavior.
`CoreGraphicsPath` exposes non-owning read-only spans to its renderer, and color-glyph paths pre-size
their paired point/type lists from the decoded contour structure.

On twenty documents containing one hundred Noto Color Emoji glyphs each, median render allocations fell
from 343,345,800 to 251,512,824 bytes (26.7%, about 17.2 MB to 12.6 MB per document). Sequential
release-mode runs measured render time falling from about 420 ms to 404 ms (about 4%); save allocations
were unchanged. Generated PDFs had the same size and were byte-identical after normalizing timestamps,
random font-subset tags, and document IDs. Focused tests cover exact path syntax and precision, the
allocation-free numeric fast path plus its large-number fallback, and pre-sized path storage. The full
net8.0 suite passed (10,548 tests, 9 platform skips), diff coverage was 97%, and the solution rebuilt
with zero warnings.
