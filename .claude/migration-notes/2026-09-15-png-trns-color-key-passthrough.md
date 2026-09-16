# PNG chroma-key (`tRNS`) transparency now also uses byte-for-byte pass-through

A PNG carrying a `tRNS` chunk — grayscale/truecolor chroma-key transparency, or a palette PNG whose
`tRNS` entries are all fully opaque or fully transparent (no partial values) — used to be excluded
from the lossless pass-through embedding added by #1086, falling back to the full decode +
`/FlateDecode` raw-RGB + separate `/SMask` path. It now also embeds via the same byte-for-byte
`/FlateDecode` pass-through as an opaque PNG, with a PDF color-key `/Mask` array (built from the
`tRNS` chunk's own bytes) marking the transparent color/index instead of a separate alpha plane —
smaller output, and pixel-exact the same way the rest of pass-through already is.

A palette PNG whose `tRNS` contains a genuine partial-alpha entry (neither 0 nor 255) is
unaffected — that still can't be expressed as PDF's binary color-key mask, so it keeps using the
existing decode+`/SMask` path, same as a PNG with a real per-pixel alpha channel (color type 4/6)
always has.
