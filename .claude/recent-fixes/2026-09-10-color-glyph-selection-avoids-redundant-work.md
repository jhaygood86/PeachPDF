# Color-glyph selection avoids redundant shaping and transient buffers

_Landed 2026-09-10._

The invisible text added for searchable/selectable COLR glyphs originally repeated shaping after
`DrawString` had already shaped the run for measurement. It also built cluster ownership through a
sorted glyph-index list and one `StringBuilder` per glyph in the ordinary disjoint-cluster case, then
created several temporary strings and byte arrays to serialize each `/ActualText` span and CID.

The color path now shapes once and reuses that result for measurement, vector painting, source
ownership, and invisible text. Disjoint clusters take a linear no-sort path and materialize their
source ranges directly; overlapping clusters retain the widest-span precedence fallback. UTF-16BE
hex strings and CIDs append directly to the content stream. Subsetting likewise reads contour counts
without copying glyph data, allocates selection bookkeeping only for fonts that need it, and writes
synthetic selection contours directly into the destination `glyf` table.

A release-mode benchmark rendering and saving twenty 100-emoji documents measured median elapsed
time falling from 888.2 ms to 800.0 ms (9.9%) and allocations from 424,484,096 to 420,372,992 bytes
(1.0%). The isolated ownership mapper fell from 3,061,600 to 727,200 bytes over one hundred
200-glyph mappings (76%). Targeted COLR/CPAL rendering and subsetting tests passed, and the generated
PDF size remained unchanged at 756,801 bytes for the benchmark fixture.

A follow-up measurement compared the four one-character appends used for each hexadecimal UTF-16
code unit with one append from a stack-allocated four-character span. After tiered JIT compilation
settled, the span version took about 52.7 ms rather than 76.3 ms over ten million code units (31%
faster, with both allocating only the benchmark's 40 bytes). Font subsetting cannot return
stack-allocated glyph data because that memory would outlive the method; it now returns a non-owning
`ReadOnlySpan<byte>` over the immutable source font and copies that directly into the final subset.
Ten full Source Sans subsets consequently fell from 14,704,528 to 11,898,752 allocated bytes (19%).
