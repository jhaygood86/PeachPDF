# Real OpenType vertical metrics (vmtx advance + VORG origin) now consulted for upright vertical text

Issues [#770](https://github.com/jhaygood86/PeachPDF/issues/770) (vmtx advance) and
[#775](https://github.com/jhaygood86/PeachPDF/issues/775) (VORG origin). `OpenTypeDescriptor
.GlyphIndexToVerticalAdvance`/`GlyphIndexToVerticalOrigin` (real `vhea`/`vmtx`/`VORG` table parsing) already
existed and were fully unit-tested, but had zero production callers — vertical-writing-mode upright text
(HTML `writing-mode: vertical-rl`/`vertical-lr` and SVG's own text pipeline) approximated every upright
character's down-the-column advance as the font's own line height, and never positioned a glyph at its real
per-glyph vertical origin at all. See `.claude/accepted-gaps/no-vertical-writing-mode-layout.md` for the
full history this closes a piece of.

## The load-bearing idea

Gate every new code path on real per-font capability flags — `RFont.HasVerticalMetrics` (`vhea`+`vmtx`
present) for advance, a *separate*, narrower `RFont.HasVerticalOrigin` (a real `VORG` table specifically,
not merely `vhea`/`vmtx`) for origin positioning. The overwhelming majority of fonts carry none of this data
at all, and each fallback (`OpenTypeDescriptor`'s own no-table defaults) is a *different* number from the
pre-existing approximations — calling either unconditionally would have silently changed output for nearly
every font in existence. Gating means the fallback branches in `CssLayoutEngine.NaturalWordSize`'s and
`FragmentPainter.Text.cs`'s `PaintUprightVerticalRun`'s upright branches (and their SVG counterparts,
`SvgRenderer.LayoutGlyphs`/`PaintUprightGlyph`) are byte-for-byte what they were before this change; only a
font that genuinely has the relevant data takes each new path. Origin positioning is deliberately gated
*more narrowly* than advance: a font with `vhea`/`vmtx` but no real `VORG` only offers `vhea.ascent` as a Y
fallback, a value not designed to mean "vertical origin" the way a real `VORG` entry is, so the positioning
shift never applies on that weaker signal — only advance does.

New capability surface added to `RFont`/`FontAdapter`, mirroring the existing CPAL-palette section's own
shape (`public virtual ... => default`, only the OpenType-descriptor-backed adapter overrides it):
`HasVerticalMetrics`/`GetVerticalAdvance`, `HasVerticalOrigin`/`GetVerticalOriginY`. Each `FontAdapter`
override uses the exact same design-units-to-pixels formula its own constructor already uses for
`_ascent`/`_height` (`font.Size * descriptor.X / descriptor.UnitsPerEm * PixelsPerPoint`) — no new
conversion convention invented. `CharCodeToGlyphIndex` (a plain cmap lookup), not full `Shape`/GSUB, matches
the existing upright code paths, which already treat each character as an atomic rune with no shaping.

## A real regression, caught only by rendering the real thing

Wiring in the real `vmtx` advance and stopping there — layout reserves the real (smaller) advance, paint
steps by the same real (smaller) advance, both sides verified to agree with each other — passed every test
written for it, including a full test-suite run. It was still visibly broken: `RGraphics.DrawString` always
paints a glyph across the font's **full line-height span** from its anchor, independent of whatever advance
the caller steps by. A real `vmtx` advance is routinely *narrower* than the font's line height (a CJK
vertical font typically advances one em per character; ascent+descent is usually well over one em) — so as
soon as the per-character step became genuinely smaller than before, each glyph's own paint started bleeding
into the next character's space, one character after another, all the way down the column. Every
layout/paint-agreement test still passed, because both sides agreed on the same too-small number —
self-consistency was never the thing that was wrong.

This was found by generating a real PDF through the public `PdfGenerator` API with real upright CJK text in
the repo's own bundled `NotoSansJPSubset.ttf`, and rasterizing it (PDFium) — exactly the repo's own
painting-test convention ("a passing test... is not proof a feature renders correctly... actually rasterize
the output and look at it"), and exactly why the convention exists. The first rendering showed two Latin
characters ("A"/"B") fully overlapping each other into an illegible smear. Bisecting against a stashed
pre-change baseline (same text, both a wrapped-narrow and an unconstrained-tall box) confirmed the overlap
was new, not pre-existing, and that a taller box (no line-wrap involved) still reproduced it — ruling out a
wrap-boundary interaction and pointing straight at the advance/paint-span mismatch above.

**The fix**: `PaintUprightVerticalRun`/`PaintUprightGlyph` now `PushClip`/`PopClip` each upright character to
its own reserved cell (`[thisCharacter'sOffset, +itsAdvance]` along the block axis) before drawing it,
whenever `HasVerticalMetrics` is true — confining a glyph whose natural paint span is taller than its own
advance cell, rather than letting it bleed into whatever paints next. The line-height fallback path needs no
clip, since its advance already equals the full painted span by construction. Re-rendering the same repro
through both PDFium and MuPDF after the clip confirmed clean, non-overlapping, fully legible output.

**A second bug, same rasterize-and-look method**: the first clip attempt used an "unbounded" cross-axis
range (`double.MinValue`/`MaxValue`) for the HTML path, and an analogous SVG version. On the SVG side this
made every upright glyph in the whole document *invisible* — the extreme coordinates overflow through
`RenderInto`'s own viewBox-to-viewport transform matrix multiply into a degenerate (empty) effective clip.
Swapped for a finite, merely-generous margin around the glyph's own position (`Math.Max(glyphWidth, fontSize)
* 8`), which needs no such transform-safety concern and was re-verified the same way.

## VORG positioning: reverted once, then actually shipped after rigorous re-derivation

A first attempt also shifted each character's paint anchor by `GetVerticalOriginY(rune) - Ascent`
(`OpenTypeDescriptor.GlyphIndexToVerticalOrigin`, itself already existing and unit-tested pre-#770). Once
combined with the clip-per-cell fix above, this cropped away most of each glyph, and was reverted with the
conclusion that "VORG-aware positioning needs a per-glyph-aware paint primitive this repo does not have
yet." **That conclusion was wrong** — it was reached under time pressure without actually reading the
OpenType spec's own defining text for what `vertOriginY` measures.

Revisited properly: fetched and read the authoritative spec
(`learn.microsoft.com/en-us/typography/opentype/spec/vorg` and the `vmtx` page it derives its geometry
from), which states outright that a glyph's vertical origin Y is "the sum of the glyph's top side bearing...
and the top... of the glyph's bounding box" — a plain Y coordinate in the glyph's own outline coordinate
system, baseline-relative, Y-**up**, not "distance from the top of the em box" (the wrong assumption behind
the first attempt). Traced `XGraphicsPdfRenderer.DrawString` precisely: the anchor point is shifted
internally by `cyAscent = lineSpace * font.CellAscent / font.CellSpace`, and confirmed directly
(`XFont.cs`) that `CellAscent = descriptor.Ascender` — the exact same field `RFont.Ascent` is built from.
Deriving from there (`penY` = this character's cell-top pen position, which per spec is exactly where a
glyph's vertical origin belongs): `point.Y = penY + vertOriginY_scaled - Ascent_scaled`. **The first attempt
had the whole correction term negated** (`y -=` instead of `y +=`) — a sign bug, not evidence that the
approach was unworkable. No new paint primitive was needed; the existing per-character `DrawString` anchor
just needed the right formula.

Gated more narrowly than advance (`RFont.HasVerticalOrigin`, requiring a real `VORG` table, not just
`vhea`/`vmtx`) precisely because the bundled test font's own `vhea.ascent`-derived positioning fallback (500,
for a font whose general `Ascender` is 1160) is a poor stand-in for a real vertical origin — even with the
corrected sign, extending the shift to that fallback would have been a real, if merely large-not-broken,
repositioning with no strong data behind it. Real `VORG` data is the one case with a spec-intended,
per-glyph-authored value to trust.

**A genuine spec gap found and fixed along the way**: the OpenType spec states `VORG` "may only be used in
CFF or CFF2 OpenType fonts" and "if present in OpenType fonts containing TrueType outline data, it must be
ignored" — `GlyphIndexToVerticalOrigin` didn't check outline format before trusting a `VORG` table.
`HasVerticalOrigin` now requires `FontFace.glyf == null` (the same signal `IsColorFont` already uses) in
addition to `vorg != null`. This touched already-passing, deliberately-designed tests built on the bundled
TrueType font (`GlyphIndexToVerticalOrigin_UsesVorgWhenPresent`) — split into a TrueType-ignores-it version
and a new CFF-honors-it version (`BundledFonts.Otf`, confirmed genuinely CFF-flavored by directly parsing
its own sfnt table directory).

Since no bundled font carries a real `VORG` table, this path is only exercisable via a synthetic `VORG`
table appended to a real font (the `VerticalMetricsTablesTests.cs` synthetic-table technique, extracted into
a shared `SyntheticFontTables.cs` helper once a second file needed it). Visually verified (PDFium + MuPDF):
a "well-behaved" synthetic origin matching the font's own `Ascender` renders flush with its cell, no visible
shift, exactly as the spec's own worked example predicts; a deliberately different origin produces a real,
consistent, non-overlapping shift (confirmed via pixel-diff between the two renders); the same synthetic
`VORG` on a TrueType font renders pixel-identical (byte-for-byte hash match) to the no-`VORG` case, proving
the CFF-only gate actually suppresses it; and the real `writing_mode` showcase content (no synthetic data,
real bundled fonts only) renders pixel-identical to before this change.

## What was deliberately not done

The rotated/`sideways` paths (`NaturalWordSize`'s rotated branch, `FragmentPainter.SidewaysRotation`, SVG's
`PaintRotatedGlyph`/`LayoutGlyphs`'s rotated branch) are untouched. This isn't scope-trimming — issue #770's
own text says so directly: "For Latin/rotated text under `text-orientation: sideways` this approximation is
visually reasonable (a rotated horizontal advance is a fair stand-in)." CSS Writing Modes' spec intent and
real browser behavior both use rotated horizontal metrics for `sideways` orientation, not vertical ones, so
there was no approximation to fix there.

## A post-change review pass found three more real bugs

The mandated 8-angle review pass (per `CLAUDE.md`'s "Post-change review pass" convention) surfaced three
confirmed defects in the #775 change, on top of the sign bug and CFF-only gap above:

1. **SVG's origin-shift computation was nested inside the wrong gate.** `LayoutGlyphs` computed
   `GlyphInfo.OriginYOffset` only inside its `HasVerticalMetrics` branch, so a font with a real `VORG` table
   but no `vhea`/`vmtx` — `HasVerticalOrigin` true, `HasVerticalMetrics` false, exactly the shape
   `BundledFonts.Otf` (CFF, no native vertical metrics) has once only a `VORG` table is appended — got no
   origin shift at all. Three independent review angles converged on this same line. Fixed by checking
   `HasVerticalOrigin` independently of `HasVerticalMetrics`, and covered by a new SVG regression test built
   on exactly that font shape.
2. **The clip-per-cell gate from the `vmtx` work didn't extend to the `VORG`-only case.** Both
   `PaintUprightVerticalRun` (HTML) and `PaintUprightGlyph` (SVG) only pushed their protective clip when
   `HasVerticalMetrics` was true — but a `VORG`-shifted glyph on a font with no vertical-metrics advance data
   (the same shape as bug 1) paints unclipped, with no reserved-cell bound at all guarding the shift. Fixed
   by extending both gates to `HasVerticalMetrics || HasVerticalOrigin`. Covered on both the HTML and SVG
   side by regression tests asserting the `PushClip`/`PopClip` calls bracket the draw.
3. **A bidi-mirrored glyph's origin offset went stale.** SVG's `ApplyBidiReordering` already recomputes
   `GlyphInfo.IsUpright` after rewriting a glyph to its mirror codepoint (a #770-review fix), but didn't
   recompute `OriginYOffset` alongside it — a glyph that becomes upright-classified only *after* mirroring
   would keep whatever offset (typically zero, never computed) it had before mirroring, instead of the
   mirrored codepoint's own real `VORG`-derived value. Fixed by recomputing both together. This one's
   precondition (RTL mirroring, an upright-classification change from the mirror, and a real `VORG` table on
   the resolved font, all simultaneously) is narrow enough that a faithful regression test would need
   per-glyph `VORG`-override support the `SyntheticFontTables` test helper doesn't have yet — verified by
   code inspection and the full existing `SvgTextBidiTests` suite passing unchanged, not by a dedicated new
   test; a reasonable next step if this area is touched again.

A fourth, non-bug review finding (the `VORG`-shifted clip window doesn't itself shift with the anchor, only
the draw position does) was investigated empirically — zoomed into rasterized PNG crops of the shifted
glyph's top edge for both a "well-behaved" and a deliberately-offset synthetic origin — and confirmed the
glyph paints fully intact in both cases, not cropped. This is a correct, deliberate design choice (the clip
window's role is bounding the *advance* cell from the `vmtx` work, an orthogonal concern to *where within*
that cell the glyph anchors), documented in `PaintUprightVerticalRun`'s and `PaintUprightGlyph`'s own remarks
rather than "fixed."

A fifth, non-bug finding (duplicated design-units-to-pixels scaling arithmetic between
`FontAdapter.GetVerticalAdvance`/`GetVerticalOriginY`) was a real simplification opportunity, not a
correctness bug — extracted into a shared private `ScaleDesignUnits` helper.

## Evidence

`dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` — full suite (8997 tests, including the
two new review-driven regression tests) passing. `dotnet build PeachPDF.slnx -t:Rebuild` — zero warnings
across the whole solution. Diff coverage 99% (812 lines, 8 missing — all pre-existing gaps in files this
change didn't touch the missing lines of). New tests: `VerticalMetricsTablesTests` (descriptor-level provenance for both `vmtx`
advance and `VORG` origin, including the CFF/TrueType split), `RFontVerticalMetricsDefaultsTests`
(base-class defaults reproduce the pre-existing approximations exactly), `TextOrientationIntegrationTests`/
`SvgTextWritingModeIntegrationTests` additions for layout reservation, paint step, the clip-per-cell
push/pop ordering, the exact `VORG`-shift formula against a synthetic-origin fixture, and the CFF-only gate
proven to actually suppress `VORG` on a TrueType font (not just untested). Beyond the automated suite: real
PDFs generated through the public `PdfGenerator` API and rasterized through both PDFium and MuPDF at every
stage of this work — what actually caught the overlap regression, the SVG clip-transform bug, and confirmed
the final `VORG` positioning behaves exactly as derived.
