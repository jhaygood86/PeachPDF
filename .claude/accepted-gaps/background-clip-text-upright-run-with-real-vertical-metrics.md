# `background-clip: text` falls back for an upright run with real vertical font metrics

Tracked as [#1194](https://github.com/jhaygood86/PeachPDF/issues/1194). Found during issue #1123's own
review.

#1123 built a vertical-writing-mode `background-clip: text` clip shape for an upright run
character-by-character (`FragmentPainter.Decorations.cs`'s `CollectUprightWord`), one `GetTextOutline`
call per character, translated to that character's own cell
(`EnumerateUprightGlyphPlacements`/`PaintUprightVerticalRun`).

**Why it's out of scope for now**: for a font with real OpenType vertical metrics
(`RFont.HasVerticalMetrics`/`HasVerticalOrigin` - `vhea`/`vmtx`/`VORG`), `PaintUprightVerticalRun` clips
each character's *paint* to its own reserved cell, because a real `vmtx` advance is routinely narrower
than the font's own line height - without that clip, ordinary painting would bleed into the neighboring
character's cell. `CollectUprightWord` has no equivalent per-cell clip on the *outline* it unions for the
clip shape - `RGraphicsPath` has no path-intersection (boolean clip) primitive at all, and building one
is a real, separate undertaking (a proper polygon-clipping algorithm, not a small addition). Unioning the
raw, unclipped outline instead would make the clip shape wider than what paint actually draws, letting
the background show through in a sliver where no glyph ink renders - worse than falling back.

`CollectUprightWord` treats such a font as an "unsupported run" (the same fallback path an outline-less
font already uses) rather than shipping an over-wide shape. Narrow in practice: it only affects a font
with real vertical metrics *and* an upright-classified run *and* `background-clip: text` all at once -
verified via the bundled real-vhea/vmtx CJK test font
(`BackgroundClipTextIntegrationTests.UprightRun_WithRealVerticalMetrics_FallsBackToPlainBorderBoxRectangle`).
