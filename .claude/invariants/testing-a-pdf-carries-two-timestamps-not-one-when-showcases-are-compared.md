# A showcase PDF carries two timestamps, not one — normalize `/M` as well as `/CreationDate`

_Testing convention._

The showcase rasterization diff is worthless unless the non-deterministic bytes are normalized first,
and the list that gets repeated — `/CreationDate`, the `/ID` array, the random font subset tag, the
annotation `/NM` GUID — is one short. A **link annotation also carries `/M`**, its modification
timestamp, written as `D:YYYYMMDDHHMMSS+00'00'`. Any showcase with a link in it therefore differs
between two runs of *identical* code.

Measured: five showcases (`acid2`, `charts_css`, `document_tree_selectors`, `svg`, `tagged_pdf`)
reported as differing at **identical byte lengths** on a `main`-versus-`main` comparison. With `/M`
normalized the same comparison is 69 of 69 identical, which is the baseline a real diff has to be
read against.

**And the list is one short again in the other direction: PDFsharp writes two more timestamps in
plain text, outside any object.** Its verbose-mode file header carries
`% Creation date: MM/DD/YYYY HH:MM:SS` and `% Creation time: N.NNN seconds` in the first 200 bytes of
every PDF, before the first object. They are not `/CreationDate`, so a normalizer built from the list
above misses them — and because *every* showcase has the header, the comparison reports **69 of 69
differing** rather than a plausible handful, which reads like a real regression rather than a broken
normalizer. Normalize both lines before believing any showcase diff.
