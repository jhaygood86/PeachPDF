# Vertical writing-mode upright text now uses a font's real vmtx advance and VORG origin, when it has them

Previously: under `writing-mode: vertical-rl`/`vertical-lr`, every upright character (`text-orientation:
upright`, or `mixed`'s upright-classified runs) advanced down the column by the font's own line height
(ascender + descender) and always anchored at the plain top of its own reserved cell, regardless of what
OpenType vertical-metrics data the font actually carried. This was a deliberate approximation — real
`vmtx`/`VORG` parsing existed but nothing consulted either.

Now:

- An upright character's down-the-column **advance** consults the font's real `vhea`/`vmtx` data when the
  font has it (`RFont.HasVerticalMetrics`) — mainly professional CJK vertical fonts, including this repo's
  own bundled `NotoSansJPSubset.ttf`. Upright text in such a font now typically renders more tightly spaced
  down the column (a real `vmtx` advance is routinely about one em, narrower than the font's own line
  height), each character clipped to its own reserved cell so it never visually bleeds into its neighbor's.
- An upright character's **anchor position** additionally consults the font's real `VORG` vertical-origin
  table when it has one (`RFont.HasVerticalOrigin`) — a narrower, separate condition from the advance above:
  a font needs an actual `VORG` table, not merely `vhea`/`vmtx`, since `VORG` is the only vertical-metrics
  data genuinely designed to mean "here is this glyph's real vertical origin." `VORG` is also CFF/CFF2-only
  per the OpenType spec — a TrueType-outline font's `VORG` table (rare in practice) is never consulted.

A font without real vertical metrics at all — the common case, essentially every Latin-oriented font and
most CJK fonts too — is completely unaffected by either change: the pre-existing line-height/top-of-cell
approximations still apply byte-for-byte. None of this repo's own bundled fonts carry a real `VORG` table,
so the origin-positioning change specifically has no effect on any currently-shipped showcase or default
output either — it only takes effect for a genuinely `VORG`-equipped font a document author supplies.
