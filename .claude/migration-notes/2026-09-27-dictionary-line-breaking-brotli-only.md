# Thai/Khmer/Lao/Burmese dictionary line breaking is Brotli-only, not DEFLATE (unreleased feature, amended before it ships)

Thai and Khmer word-list line breaking (UAX #14 Complex_Context / `SA` class) has not shipped in a tagged release
yet — it does not exist at `v0.9.20`, the most recent tag (`git show v0.9.20:src/PeachDrawing.Text/Internal/Text/Segmentation/WordDictionary.cs`
finds no such file there). This note is not a regression from a released behavior; it documents an amendment to
work still on `main`, so the next release's notes describe the feature's final shape directly rather than a
DEFLATE version followed immediately by a Brotli one.

**What the unreleased dictionary feature looked like before this PR:** the four word-list dictionaries were
embedded as raw DEFLATE specifically so a WebAssembly host with no Brotli decoder could still read them — every
other Unicode/hyphenation resource in `PeachDrawing.Text` was already Brotli and degraded to empty/unhyphenated
there, but the dictionaries were the one deliberate exception (recorded in
`.claude/invariants/text-segmentation-tables-are-generated-and-checked-against-the-unicode-conformance-files.md`,
now corrected). Only Thai and Khmer existed; Lao and Burmese were deferred for a separate reason (nupkg size, see
below).

**What ships instead:** all four dictionaries (Thai, Lao, Khmer, Burmese) are Brotli-compressed, for consistency
with the rest of `PeachDrawing.Text.Data`'s resources and a measured 6-8% size saving per script over DEFLATE. A
host whose `System.IO.Compression.Brotli` throws `PlatformNotSupportedException` — WebAssembly in a browser, at the
time of writing — gets an empty dictionary for all four scripts (UAX #14 rule LB1's fallback: no break opportunity
inside a run of Complex_Context text), the same fallback every other Unicode table in the package already gets
there. A new pluggable decoder seam, `PeachDrawing.Text.Compression.BrotliDecompression.SetDecompressor`, lets a
host register a managed Brotli implementation to recover this data (and WOFF2 font loading, similarly affected)
instead — something the DEFLATE-only exception had no equivalent of.

**Why the exception was worth removing:** the dictionaries moved, along with every other Unicode/hyphenation
resource, into a new sibling package (`PeachDrawing.Text.Data`) specifically to stop the whole set being tripled in
the nupkg (once per `PeachDrawing.Text` target framework) — see
`.claude/recent-fixes/2026-09-27-text-data-split-into-its-own-package-and-unified-to-brotli.md`. That tripling was
also the reason Lao and Burmese were deferred in the first place; with it gone and Brotli smaller than DEFLATE, both
scripts are folded in in the same change rather than left for a follow-up.
