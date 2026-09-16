# `text-decoration-skip-ink` finds no ink under a CID-keyed CFF outline or a fallback face

Tracked as [#1074](https://github.com/jhaygood86/PeachPDF/issues/1074).

`text-decoration-skip-ink` (css-text-decor-4 §2.5) interrupts an underline or overline where it
crosses glyph ink, measured by `GraphicsAdapter.GetInkCrossings`. Two runs report *no ink known* and
so keep an unbroken line:

- **A CID-keyed CFF font.** An ordinary (non-CID) CFF/OpenType (`.otf`) font's ink is measured fine —
  `TryGetGlyphOutline` (`OpenTypeDescriptor.cs`) falls back to `Type2CharstringInterpreter` when a
  font has no `glyf` table (issue #1117), closing what used to be a blanket CFF gap. A CID-keyed CFF
  font (the `ROS` operator present in its Top DICT - common in CJK "Pro"/Source Han/Noto Sans CJK OTF
  builds) still has no outline: `CffTable` recognizes `ROS` and reports `IsSupported = false` rather
  than guessing at the wrong (top-level, likely absent) local subrs — see
  [cid-keyed-cff-font-outlines-unsupported.md](cid-keyed-cff-font-outlines-unsupported.md).
  `GetInkCrossings` returns `null` rather than an empty list precisely so this stays distinguishable
  from "this run genuinely crosses nothing" — but the painter's response to both is the same, because
  there is nothing better it could do.
- **Per-codepoint font fallback.** `GetInkCrossings` shapes through the font it is handed, exactly
  as `GetTextOutline` does, and neither follows the fallback that happens below the adapter. A
  codepoint painted by a fallback face contributes no ink.

**`auto` is conformant here and `all` is not.** §2.5 gives `auto` — the initial value, so what
almost every document gets — to user-agent discretion ("may interrupt"), and declining is a valid
exercise of it. `all` says "must interrupt", and under either case above it behaves like `none`.
That narrower deviation is what #1074 tracks.

**Do not close this by special-casing the painter.** Neither case is a decision the painter can
make differently; both are the adapter having no outline to report. Closing the first fully means
`FDArray`/`FDSelect` parsing in `CffTable` to pick the right per-glyph local subrs for a CID-keyed
font — see the linked gap note. Closing the second means the per-codepoint face selection becoming
visible at the adapter boundary, which is shared with SVG text-as-outline (`SvgRenderer`) and would
change that path too. Both are worth doing on their own terms; neither belongs inside a decoration
change.

A third, much smaller consequence of the same measurement: `GlyphInkScanner` samples the band with a
fixed number of scanlines rather than solving each segment analytically, so a stroke crossing a
*wide* band very obliquely can be reported as several spans instead of one. Harmless in practice —
a real band is a line's own thickness, a fraction of an em, and the painter dilates every span by
its skip clearance before subtracting, which merges them. The scanner's own tests pin both the thin-
band (one span) and wide-band (several) behaviours so a future reader can tell which is which.
