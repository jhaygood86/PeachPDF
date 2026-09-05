# Port HarfBuzz's real GPOS cursive attachment formula; verify against a real cursive Arabic font

Follow-on to [2026-09-04-arabic-mark-attachment-survives-display-reversal.md](2026-09-04-arabic-mark-attachment-survives-display-reversal.md),
which left `GposPositioner.ApplyCursiveAttachment` (GPOS Type 3) explicitly unfixed because the bundled
Arabic test font doesn't define a `curs` GPOS feature at all, so there was no real-font way to verify a
fix. Fixed in the same session once asked to "fix it, and use an open source font to verify it."

## What was found by running it, not by reading it

Found and bundled a real open-source font that actually needs cursive attachment: "Aref Ruqaa" (OFL 1.1,
Google Fonts) - unlike Noto Sans Arabic (positional GSUB substitution only), Ruqaa's own flowing
calligraphic connections rely on `curs` layered on top of its own `isol`/`init`/`medi`/`fina`. My first
attempt applied the existing (already-reversal-safety-patched, via a new `ShapedGlyph.AttachedToIndex` on
the entry glyph) formula against it and rasterized the result: every multi-letter word rendered **blank**.
Diagnosing why (`word.Width` was ~0, sometimes exactly 0) traced back to `TryApplyCursivePair`'s own
formula - `xDelta = i.XOffset + exit.X - entry.X - j.XOffset - intermediateAdvance`, applied as
`i.XAdvanceDelta += xDelta` - producing a large negative advance (verified arithmetically correct against
this font's own raw anchor data via `fontTools`, so not a reader bug) that collapsed the whole word's
measured width toward zero for essentially every letter pair tested (a dozen 2-3 letter combinations
scanned, all near-zero).

That formula was never actually checked against a real, independent implementation - it was derived
directly from the OpenType spec's own prose ("adjusts the x-coordinate ... so the two points coincide"),
which is genuinely ambiguous about *how*. Installed `uharfbuzz` (real HarfBuzz's own Python binding) and
shaped the exact same text through the exact same font file: HarfBuzz's own total x-advance for "تب"
was 805 design units - exactly the plain, uncorrected sum of the two letters' own nominal widths, with
**no** advance/offset correction on either glyph. Fetching HarfBuzz's actual C++ source
(`OT/Layout/GPOS/CursivePosFormat1.hh`) explained why: HarfBuzz's real algorithm is nothing like a
spec-prose reading of "connect the two points" - for RTL-direction text specifically, it treats the two
glyphs' corrections as **independent**, each depending only on that glyph's own anchor:

```
d = exit_x(i) + pos[i].x_offset
pos[i].x_advance -= d;  pos[i].x_offset -= d      // i's own correction - self-contained
pos[j].x_advance = entry_x(j) + pos[j].x_offset   // j's own correction - REPLACES its advance, self-contained
```

Neither line references the *other* glyph's position or the pen-distance between them at all - the prior
"connect exit to entry via one combined cross-glyph delta" formula was simply the wrong model.

## Load-bearing idea

**Port the real algorithm instead of re-deriving a second one from spec text.** Rewrote
`GposPositioner.TryApplyCursivePair`'s main-direction (X) correction as a direct, attributed port of
HarfBuzz's `HB_DIRECTION_RTL` branch (the only branch needed - see below). The cross-direction (Y)
correction was already correct (independently re-derived from spec text and never contradicted by
HarfBuzz's own chain-attachment logic) and needed no change.

- This also **simplified** the reversal-safety work from the prior fix: because each glyph's correction
  now depends only on its own anchor (never the other glyph's position), it is automatically safe under
  `OpenTypeDescriptor.ReverseGlyphsForDisplay`'s plain interval-mirror - no `AttachedToIndex` tracking
  needed for cursive attachment at all (unlike mark attachment's `XOffset`, which genuinely does encode a
  directional relationship). The `AttachedToIndex = i` assignment the prior fix added to
  `TryApplyCursivePair` is removed; the field still exists, used only by `ApplyMarkAnchor`.
- Hardcoded the `HB_DIRECTION_RTL` formula unconditionally, rather than threading a "buffer direction"
  concept through `GposPositioner` the way HarfBuzz's own buffer carries one: the *only* caller of this
  whole lookup type is Arabic-family joining (`curs` is requested exactly when a run carries resolved
  `JoiningForms`, and only then), which is always RTL-treated in this codebase (see the prior fix's own
  finding that Arabic is intrinsically RTL regardless of the paragraph's own `direction`). The lookup's
  own `lookupFlag` `RIGHT_TO_LEFT` bit - a separate concept from buffer direction - still governs Y-offset
  target and outer iteration order exactly as before.
- `pos[j].x_advance = entry_x + x_offset` is a plain **assignment**, not an adjustment - it replaces
  whatever j's advance was (including any earlier kerning correction on the same glyph), matching
  HarfBuzz's own semantics exactly rather than softening it into an addition that would have been easier
  to justify in isolation but wouldn't match real output.

## What was deliberately not done, and why

- No LTR (buffer-direction) branch of the main-direction formula - unreachable given current wiring (see
  above), and adding untested code for a case nothing can currently trigger would be exactly the kind of
  "fixed" surface this bug came from in the first place.
- No port of HarfBuzz's `attach_chain`/`propagate_attachment_offsets` graph-based resolution machinery
  (used for deep multi-level attachment chains, e.g. mark-on-mark-on-cursive-glyph). Not needed: this
  codebase's own sequential per-pair mutation (processing pairs in an order that already guarantees a
  glyph's Y-offset is fully resolved before anything reads it) achieves equivalent cascading for the
  chain depths real Arabic/Ruqaa text actually produces, confirmed by the 6-letter "بيتالف" real-font
  test rendering correctly end-to-end.

## Evidence

- New bundled font: `assets/fonts/ArefRuqaaSubset.ttf` (+ `.LICENSE.txt`, OFL-1.1) via
  `assets/fonts/generate_aref_ruqaa_subset.py` - real `curs` data retained (50 covered glyphs, 32 entry +
  37 exit anchors after subsetting), `BundledFonts.ArabicCursive`.
- New test file `ArabicCursiveAttachmentCharacterizationTests.cs`: a plausible-positive-width regression
  guard (the pre-fix bug's own signature) for both letter orderings, an exact-value pin cross-checked
  against real HarfBuzz's own reported total advance (805 design units for "تب"), and a Latin-text
  no-op guard.
- Updated `GposCursiveMarkLigatureSyntheticTests.cs`'s two pre-existing synthetic tests (their old
  expected values encoded the wrong formula; their own "connection coincides" self-consistency check
  encoded the wrong formula's own assumption and doesn't hold under the correct one - removed rather than
  patched, since a two-glyph interval "meets in the middle" isn't actually a HarfBuzz invariant, just an
  artifact of the earlier wrong design).
- Two-renderer (PDFium + MuPDF) rasterization of "ت"/"بت"/"تب"/"بيتالف"/"لا" through Aref Ruqaa: every
  word now renders as a single connected, correctly-shaped calligraphic run in both renderers, matching
  what the font is actually designed to look like - the pre-fix render showed only the two single-letter
  cases (no cursive pair) with every multi-letter word blank.
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`: full suite green (post-fix
  count includes the new test file and the two corrected synthetic tests), 0 failed.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors.
