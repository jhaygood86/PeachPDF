# Fix GPOS mark-to-base/ligature/mark positioning under display-order glyph reversal

Follow-on to [2026-09-04-arabic-rlig-logical-order-shaping.md](2026-09-04-arabic-rlig-logical-order-shaping.md),
which fixed lam-alef's `rlig` never firing but flagged a second, distinct visual defect found in the
same rasterization pass: a Beh/Teh combining-mark dot rendering tens of points away from the letter it
belongs to. Root-caused and fixed in the same session, once "dig into that mark-positioning bug next"
was requested.

## The bug

`GposPositioner.ApplyMarkAnchor` (backing GPOS Types 4/5/6 - mark-to-base, mark-to-ligature, mark-to-mark)
computes a mark glyph's `XOffset` as `baseAnchor.X - markAnchor.X - intermediateAdvance`, where
`intermediateAdvance` is the pen-distance from the base to the mark *under the walk order GPOS actually
ran in* (true logical order, always - GSUB/GPOS need real logical adjacency, see the linked fix). That
subtraction only cancels out correctly if painting later walks the glyphs in that *same* order. Once
`OpenTypeDescriptor.ReverseGlyphsForDisplay` reverses the list for an Arabic-family joining word's
display, painting walks a *different* order - the mark's new neighbors, and so its own natural pen
position relative to its base, are nothing like what the offset was computed for. A plain
`glyphs.Reverse()` (what the code did before this fix) carries the *old* offset value unchanged into the
*new* walk order, silently mis-positioning every attached mark by roughly its own base glyph's advance
width.

This reproduced dramatically for "تب" (Teh + Beh): Beh's own final ("fina") form is a long connecting
swash with an unusually wide ~1093-design-unit advance, so its own dot rendered roughly 34pt away from
the letter - visually a disconnected floating dot with no apparent base. The same defect exists for every
combining mark this font decomposes via `ccmp` (Beh, Teh, Yeh, Feh all use it), just less dramatically for
narrower base forms - "بيتالف"'s garbled middle letters (noted as unexplained in the linked fix) turned
out to be this same bug, not a separate one.

## Load-bearing idea

**A plain list reversal is mathematically correct for a glyph's own natural (advance-based) position -
it is not correct for an offset that encodes a *relationship to a specific neighbor*.** Proved by direct
calculation before writing any fix: reversing a list and re-walking it with fresh left-to-right pen
accumulation is exactly equivalent to reflecting every glyph's own `[origin, origin+advance]` interval
around the run's total width (verified algebraically for glyphs with zero offset - the two operations
give identical results). That reflection is exactly right for an independent glyph, and exactly wrong for
a mark, because "align my anchor with my predecessor's anchor" is a directional relationship that
reversal inherently flips (the base was the mark's predecessor before reversal; after, it's very often the
mark's *successor* instead) - no generic coordinate transform on absolute positions can repair a
relationship that depends on *who is adjacent to whom*, only recomputing the relationship against the new
adjacency can.

- `ShapedGlyph` gained `int? AttachedToIndex` - the glyph-list index (of the pre-reversal list; GPOS never
  inserts or removes glyphs, so this stays valid for the lifetime of one `Shape()` call) that
  `ApplyMarkAnchor` anchored this glyph to. Recorded unconditionally (cheap, and harmless for every other
  caller that never reverses).
- `OpenTypeDescriptor.ReverseGlyphsForDisplay` now does the reversal in two passes instead of one:
  1. Before reversing, resolve every glyph's *desired* absolute display-X: for an unattached glyph, the
     interval-mirror position (`totalWidth - logicalAbsoluteX - ownAdvance` - proven correct above); for
     an attached mark (`AttachedToIndex` set), its base's own *resolved* desired position plus the *same*
     relative offset it had from that base in logical order (`logicalAbsoluteX[mark] - logicalAbsoluteX[base]`)
     - a purely geometric relationship that must not be disturbed by reversal, resolved recursively (with
     a depth guard mirroring `GsubShaper.MaxNestedContextDepth`'s own rationale) so a mark-on-a-mark chain
     resolves through its own base first.
  2. Reverse the list, then walk it computing each glyph's *new* natural cumulative position and setting
     `XOffset` to whatever reproduces its already-resolved desired position under the new order.
  `YOffset` needs no equivalent fix - it was never pen-position-dependent (vertical placement doesn't
  accumulate along the line the way horizontal advance does), confirmed directly against the font's own
  raw GPOS anchor data (`ApplyMarkAnchor`'s Y formula has no `intermediateAdvance` term at all) before
  writing any code, not assumed.

## What was found by running it, not by reading it

- The exact numeric signature of the bug (a mark's `XOffset` off by very close to its own base's advance
  width - `-706`/`-748` design units for Beh's ~1093-unit-wide `fina` form) was found by adding a temporary
  diagnostic test dumping every shaped glyph's `ClusterStart`/offsets/computed pen position, then
  cross-referencing the *raw* GPOS anchor coordinates directly from the font via `fontTools` (not
  PeachPDF's own reader, to rule out a reader bug first) - confirming the *offset value itself* was
  arithmetically consistent with the font's own anchor data under the *pre-reversal* walk order, which is
  what pointed at reversal-order-dependence as the root cause rather than a wrong anchor lookup.
  Extracting and decoding the raw PDF content stream's `Td`/`Tj` operators directly (glyph IDs and exact
  page coordinates) was what finally proved conclusively that the *base* glyphs were painting correctly
  (two Beh/Teh words' connected outlines were pixel-identical in shape, just carrying different dot
  glyphs) and only the *mark* was mispositioned - a purely visual read of the rasterized page had
  initially and wrongly suggested Beh's entire base glyph was "missing," which the raw coordinate data
  disproved.
- Confirmed the font's own contextual "wide" glyph-variant selection (a `rlig`-tagged Format-3 chaining
  rule promoting `uni066E.medi`/`.init` to `.medi.wide`/`.init.wide` whenever immediately followed by
  either of the "two dots" mark glyphs, to make room for the wider mark) was firing correctly and is
  *not* a bug - initially suspected as a possible second defect, ruled out by dumping the font's own GSUB
  feature/lookup tables directly via `fontTools` rather than assuming the substitution was wrong just
  because the resulting glyph name ("`.wide`") was unexpected.

## What was deliberately not done, and why

- `GposPositioner.ApplyCursiveAttachment` (GPOS Type 3) has the same class of problem in a different
  mechanism - it corrects position via `XAdvanceDelta` on the *preceding* glyph (so a *later* glyph's
  natural pen position lands correctly), which is equally walk-order-dependent and would equally break
  under reversal. Not fixed here: the bundled Arabic test font (a Noto Sans Arabic subset) does not define
  a `curs` GPOS feature at all, so this exact defect has no real-font reproduction yet, and the PR2-era
  work already flagged this lookup's own RTL-cascade behavior as unverified pending real cursive-attachment
  font coverage. Fixing it blind, without a way to rasterize and confirm the fix against a real cursive
  Arabic font (Nastaliq-style fonts are the common real-world case), risks introducing an equally-unverified
  "fix" in its place. Left as a known, narrower follow-up rather than silently left broken - flag before
  any font that actually exercises cursive attachment (e.g. an Urdu Nastaliq showcase) is added.

## Evidence

- New test: `ArabicJoiningCharacterizationTests.EndToEndLayout_MarkStaysAttachedToItsOwnBaseAfterDisplayReversal`
  (both `"تب"` and `"بت"` orderings) - walks the actual shaped, display-order glyph list computing each
  glyph's absolute X exactly as painting does, and asserts every combining-mark glyph lands within half
  an em of an adjacent glyph (comfortably separating "correctly attached" from the pre-fix bug, which was
  off by up to a full base-glyph advance) - a structural/positional assertion per this repo's own stated
  distrust of content-stream-substring-only tests, not a token-presence check.
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`: 9644 passed, 9 pre-existing
  platform-specific skips, 0 failed (full suite).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors.
- Two-renderer (PDFium + MuPDF) rasterization of every case from the linked fix's own evidence, plus
  isolated single/two-letter Beh/Teh/Yeh/Feh words - every mark now renders attached to its own letter in
  both renderers; "بيتالف" (previously visually garbled in its middle letters) now renders as a single,
  correctly-dotted connected word.
