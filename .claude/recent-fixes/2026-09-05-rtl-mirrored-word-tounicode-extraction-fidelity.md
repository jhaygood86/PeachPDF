# Fix ToUnicode text extraction for a mirrored RTL word (parentheses swap identity, not just position)

Closes the third of the three items flagged as follow-on work once real Arabic-family joining/USE
testing existed (see [2026-09-05-devanagari-use-syllable-reordering.md](2026-09-05-devanagari-use-syllable-reordering.md)
and its own Arabic-shaping predecessors) - the plan had flagged this as "worth instrumenting/testing"
but never actually tested. It turned out to be a real, confirmed defect, not a dormant non-issue. A
code-review pass on the initial HTML-only fix found the identical, previously-undocumented bug in SVG
`<text>`, which is fixed here too rather than filed as a separate follow-up.

## The bug

`CssLayoutEngine.MirrorWordTextIfNeeded` mutates a plain (non-Arabic-joining) RTL word's own `Text` in
place via `CssRectWord.ReplaceText(BidiMirrorResolver.ApplyMirroring(PreMirrorText, level))` - UAX#9 L2
(whole-run reversal) + L4 (mirroring, e.g. `(` ↔ `)`) applied once bidi placement resolves the word's
final visual position. Painting (`FragmentPainter.Text.cs`'s `DrawWordGlyphs`) then shaped and drew
`word.Text` - correct for the glyphs actually painted on the page - but `RGraphics.DrawString` fed that
same *already-mirrored* string straight into `CMapInfo.AddShapedText`, which built each glyph's
ToUnicode CMap entry from `text.Substring(glyph.ClusterStart, glyph.ClusterLength)`. For a mirrored
character this records the glyph's *own* (mirrored) value, not the *true logical-order* character it
stands in for - copy/pasting or searching a parenthesized RTL word out of the resulting PDF recovers the
wrong text, most visibly for parentheses (which don't just move position under mirroring, they swap
identity with each other). `SvgRenderer.ApplyBidiReordering` has the exact same defect for SVG `<text>`,
via its own independent bidi/mirroring implementation (SVG's text/bidi pipeline is entirely separate
from HTML's).

## Load-bearing idea

**`CMapInfo.AddShapedText`'s `logicalText` parameter should require only that it be *positionally
aligned* with the painted `text`** - same length, same UTF-16 index per glyph cluster - rather than
requiring a specific *transform relationship* (a whole-string reversal) between the two. The first
working version of this fix took the narrower path (a reversal formula baked into `CMapInfo` itself,
justified by `BidiMirrorResolver.ApplyMirroring` being a pure whole-run reverse-then-mirror operation),
which was correct for HTML but had no way to serve SVG: SVG's bidi pass (`ApplyBidiReordering`)
physically reorders individual `GlyphInfo` list entries rather than reversing a string, so no single
"reverse by this formula" relationship exists between what SVG paints and its logical source. Moving to
a plain positional-alignment contract - `CMapInfo` just reads the same cluster range from whichever
string (`text` or `logicalText`) the caller hands it - let each caller build that alignment however its
own transform actually works, and turned the "does this generalize to a second caller" pressure point
(explicitly flagged in review as an architecture risk of the first version) into a non-issue.

- `CMapInfo.AddShapedText`'s `logicalText` contract: when non-null, the same length as `text`, and
  different from it, each shaped glyph's ToUnicode destination is read from the identical cluster range
  in `logicalText` instead of `text` - the exact same substring call, just choosing which string to read.
  `null` (or equal to `text`) is a complete no-op, identical to every call site's behavior before this
  parameter existed.
- New `BidiMirrorResolver.ReverseRunes`: the position-only half of `ApplyMirroring`'s reverse-then-mirror
  pass (reverse by `Rune`, no mirror substitution) - lets a whole-run-reversal caller (HTML, margin-box
  content) build a positionally-aligned logical source from its own stable pre-transform text: reversal
  alone already recovers *position*; mirroring only changes a character's *value*, so skipping it is
  exactly what "give me the true original character at each visual position" needs.
- Threaded as a new parameter through the whole `DrawString` call chain (`RGraphics` → `GraphicsAdapter` →
  `XGraphics` → `IXGraphicsRenderer`/`XGraphicsPdfRenderer` → `PdfFont.AddShapedText` → `CMapInfo`), as a
  **new virtual overload with a default forwarding body** on `RGraphics.DrawString` rather than adding the
  parameter to the existing abstract 8-arg overload - avoids touching any of the ~13 existing `RGraphics`
  overrides (production `GraphicsAdapter` plus every test-only mock), since only `GraphicsAdapter` (the
  one real PDF-writing backend) needs to act on it.
- Deliberately **not** added as a field on `TextShapingFeatures`, even though that record already flows
  through the whole shaping pipeline: it's used as a `ConcurrentDictionary` cache key in
  `GsubShaper.LookupIndexCache`, and a per-word-varying string field would cache-miss that lookup for
  every plain RTL word under what the code's own comments call its widest, most frequently hit critical
  section - a real perf regression for RTL-heavy documents. A separate parameter avoids this entirely.
- HTML call sites: `FragmentPainter.Text.cs`'s `DrawWordGlyphs` passes
  `BidiMirrorResolver.ReverseRunes(rectWord.PreMirrorText)` whenever the word was actually mirrored
  (`PreMirrorText != Text`) and `word.FirstLineText` is null (a `::first-line` override is its own
  independently-mirrored string derived from `OriginalText`, not from `PreMirrorText` - no known logical
  source to recover for it, so it's left `null`, same as before). `PaintUprightVerticalRun` (vertical
  writing mode's per-character paint path) threads the same already-aligned string through unchanged -
  each painted character's logical source is simply that same rune position, no reversal math needed a
  second time. `MarginBoxRenderer.ResolveBidiText` gained an `out string? logicalText`, populated via
  `ReverseRunes` only when the whole margin-box `content` string reordered as a single RTL run; a mixed
  multi-run reorder (embedded LTR text inside an RTL paragraph) leaves it `null`, since `ReverseRunes`
  alone can't reproduce a per-run reorder-and-concatenate's alignment.
- SVG fix: `SvgRenderer.GlyphInfo` gained `LogicalGlyph` (null unless this glyph was bidi-mirrored, in
  which case it's the pre-mirror character) - set in `ApplyBidiReordering` right before `Glyph` itself is
  overwritten with the mirror image. Unlike HTML, no reversal formula is needed at all: SVG's reordering
  already physically moves each `GlyphInfo` to its final visual position, so `LogicalGlyph` is already
  correctly positioned by construction. `PaintGlyphs`' batching loop builds a parallel `logicalBuilder`
  alongside its visual-text `builder` (`gc.LogicalGlyph ?? gc.Glyph` per glyph, same order, collapsed to
  `null` if nothing in the batch was mirrored); `PaintRotatedGlyph`/`PaintUprightGlyph`/
  `PaintGlyphAlongPath` (the three single-glyph-per-call paint paths - explicit `rotate=""`, vertical
  writing mode, `<textPath>`) each pass `start.LogicalGlyph`/`gi.LogicalGlyph` directly.

## What was deliberately not done, and why

- Arabic-family joining words (`EffectiveJoiningForms` non-null) never mutate `Text` at all (see
  [2026-09-04-arabic-rlig-logical-order-shaping.md](2026-09-04-arabic-rlig-logical-order-shaping.md)) -
  `PreMirrorText` always equals `Text` for them, so the `PreMirrorText != Text` guard already treats them
  as a no-op; this fix is really only load-bearing for plain RTL scripts (Hebrew and similar).
- No attempt to recover a logical source for a truncated `text-overflow` substring or a synthesized "…"
  glyph (the other two `DrawWordGlyphs` call sites, in `FragmentPainter.TextOverflow.cs`) - neither is the
  word's own full text, so there's no well-defined logical-order source to hand back; both keep
  `logicalText: null`, unchanged from before.
- SVG's gradient/pattern-fill and stroked-text paint path (`PaintTextGlyphs`'s outline branch) needs no
  `logicalText` at all - it paints via `GetTextOutline` into a filled/stroked vector path, never a real
  PDF text-show operator, so there is nothing for a ToUnicode CMap to attach to (already documented as
  "not selectable" before this fix).

## Evidence

- New `CMapInfoLogicalTextTests.cs`: exercises `CMapInfo.AddShapedText`'s contract directly, including a
  case where a GSUB ligature merges two source characters into one glyph (`ClusterLength > 1`) - not just
  the 1-character-per-glyph case the parenthesis example alone would exercise. Confirmed to FAIL without
  the fix (temporarily forcing the no-remap path) before restoring it.
- New `RtlToUnicodeIntegrationTests.cs`: end-to-end through real HTML layout + paint, covering both the
  whole-word horizontal/sideways-rotated path (`<bdo dir="rtl">(AB)</bdo>`) and the per-character upright
  vertical-writing-mode path (`writing-mode:vertical-rl; text-orientation:upright`) - the latter added
  after a review pass found the first version of this fix never wired `logicalText` into
  `PaintUprightVerticalRun` at all. Both confirmed to FAIL without their respective wiring before
  restoring it.
- `MarginBoxRendererBidiTests.cs` extended with a mirrored-punctuation case (distinguishing the new
  positionally-aligned contract from a plain Hebrew case where every character happens to be unaffected
  by mirroring) and a dedicated mixed-run case (`"שלום Latin עולם"`) asserting `logicalText` stays `null`
  for a multi-run reorder.
- New `SvgTextBidiTests.cs` cases: a batched-run mirrored-parenthesis case
  (`direction="rtl" unicode-bidi="bidi-override"`) and an explicit-`rotate=""` per-character case,
  confirming both `PaintGlyphs`' batching path and `PaintRotatedGlyph`'s single-glyph path recover the
  correct logical source. Confirmed to FAIL without the SVG-side fix before restoring it.
- Extended the shared `RecordingGraphics`/`TestRecordingGraphics` test mocks (`TestSupport/`) with
  `logicalText` recording rather than adding a third, parallel `RGraphics` test double - a review pass
  flagged a first-draft private mock in `RtlToUnicodeIntegrationTests.cs` as exactly the kind of
  duplication `RecordingGraphics`'s own doc comment already warns against.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors.
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`: 9722 passed, 9 pre-existing
  platform-specific skips, 0 failed (full suite).
