# CID-keyed CFF fonts have no decodable glyph outline

Tracked as [#1122](https://github.com/jhaygood86/PeachPDF/issues/1122).

`Type2CharstringInterpreter`/`CffTable` (added for issue #1117, `background-clip: text`) decode glyph
outlines for ordinary (non-CID-keyed) CFF/OpenType-CFF fonts, closing what used to be a blanket "no CFF
outlines at all" gap. A CID-keyed CFF font - one whose Top DICT carries the `ROS` operator, common in
CJK "Pro"/Source Han Sans/Noto Sans CJK OpenType-CFF builds - needs `FDArray` (an INDEX of per-glyph
Font DICTs) and `FDSelect` (a per-GID selector) to resolve which Private DICT/local Subrs INDEX applies
to a given glyph; `CffTable` does not parse either, so it reports `IsSupported = false` for such a font
(`IsCidKeyed` is still recognized, so a caller could name the reason) rather than guessing at the wrong
(top-level, likely absent) local subrs.

**Affects every feature built on `RGraphics.GetTextOutline`**: `background-clip: text` falls back to a
plain `border-box` clip; SVG gradient/pattern `fill`/`stroke` and `<textPath>` on `<text>` fall back to a
solid fill (see [svg-outlined-text-textpath-residuals.md](svg-outlined-text-textpath-residuals.md));
`text-decoration-skip-ink` reports "no ink known" and keeps an unbroken decoration line (see
[skip-ink-finds-no-ink-under-cff-outlines-or-font-fallback.md](skip-ink-finds-no-ink-under-cff-outlines-or-font-fallback.md)).

Not a regression in any of the three cases above - each already degraded to exactly this fallback for
*every* CFF font before #1117, and now does so only for the CID-keyed subset.
