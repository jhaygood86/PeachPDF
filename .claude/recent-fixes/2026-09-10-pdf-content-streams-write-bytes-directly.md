# PDF content streams write raw bytes directly

_Landed 2026-09-10._

`XGraphicsPdfRenderer` formerly accumulated every page or form in a `StringBuilder`, materialized one
large string at close, and raw-encoded that string into a new byte array. For a 100-emoji document this
character pipeline remained the largest allocation source after path formatting stopped boxing its
coordinates.

The renderer now writes into `PdfContentWriter`, a chunked raw-byte accumulator. Small streams begin
with a 256-byte chunk while larger streams grow to fixed 16 KiB chunks, avoiding both a large minimum
allocation and large-object-heap growth copies. Existing composite-format call sites use one reusable
scratch `StringBuilder`; path coordinates still use the exact invariant custom numeric format, then copy
their stack-formatted characters directly into the byte chunks. Closing a renderer performs one
exact-sized byte copy into the `PdfStream`, with no whole-content `char[]`, string, or encoding pass.

A tempting direct UTF-8 fixed-point formatter was rejected after randomized equivalence testing found a
last-decimal difference for ordinary coordinates: standard `F` formatting plus zero trimming is not
identical to the existing `0.####` custom format.

Against the preceding path-optimization commit, twenty 100-emoji documents reduced median render
allocations from about 251.5 MB to 150.2 MB (40.3%, or 12.6 MB to 7.5 MB per document) and rendered about
10% faster. Save allocations remained about 3.1 MB per document. The generated PDF size stayed 756,801
bytes and its bytes matched after normalizing timestamps, random font-subset tags, and document IDs.
A follow-up allocation trace measured `char[]`, `string`, and `StringBuilder` volume falling by roughly
90%, 82%, and 91%, respectively; byte arrays rose as expected because they now are the primary content
storage.

The anonymous-box allocation ratio guard moved from 2.35x to 2.55x. The writer reduces the common plain
block allocation slope more than the list-specific slope, moving the healthy ratio to 2.36x even though
both document shapes allocate less; the known no-fast-path regression remains clearly separated at
2.87x.

Verification covered all 10,550 runnable net8.0 tests, 100% diff coverage across 146 changed production
lines, and a full solution rebuild with zero warnings. The final independent diff review found no
actionable issues.
