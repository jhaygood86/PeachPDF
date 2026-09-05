# PR3 Phase 2/3 completion: shape Arabic-family joining words in true logical order, reverse only the glyph list

Closes the pipeline-wiring gap the previous entry (`2026-09-04-arabic-joining-shaper-gsub-wiring.md`)
left open, and fixes a real visual defect found while doing it: a real font's own `rlig` ligature (e.g.
Noto Sans Arabic's lam-alef) silently never fired for real Arabic text, because the word it was asked to
shape had already been bidi-reversed.

## The bug, and why it was missed until now

Arabic is a strong-R bidi run per UAX #9 and gets L2-reversed for display *regardless of the paragraph's
own `direction`* - even under `direction: ltr`. `CssLayoutEngine.MirrorWordTextIfNeeded` was calling
`CssRectWord.ReplaceText` (character-level reversal, the same treatment every plain RTL word like Hebrew
gets) for a joining-forms word too, then reversing `EffectiveJoiningForms` in lockstep so per-position
substitution stayed aligned. Positional forms (isol/init/medi/fina) are a plain per-position 1:1 swap, so
they still came out correct either way - but a real font's `rlig` feature is commonly a Format-3
*contextual* rule keyed on true logical adjacency (lam.init immediately followed by alef.fina); reversed
to alef-then-lam, that pattern can never match. Every existing test before this fix shaped Lam+Alef
directly in true logical order (never through the actual layout pipeline's own reversal), so nothing
caught it - diagnosed only once an end-to-end HTML→layout→shape round trip was added and rasterized, per
this repo's own stated distrust of self-consistent tests never checked against a real render.

## Load-bearing idea

**Shape in true logical order always; reverse only the resulting glyph list, at the very end, once
GSUB+GPOS have both already run.** This is what HarfBuzz itself does (features apply in logical order,
the buffer reverses only as the final RTL step) - not a PeachPDF-specific workaround.

- `CssRectWord` no longer has a mutable, reversible `_joiningForms` field - `EffectiveJoiningForms`
  simply returns the stable, construction-time `_logicalJoiningForms` always. A new `DisplayOrderReversed`
  bool (set by `MarkDisplayOrderReversed`) replaces the old array-reversal call; `Text` itself is never
  mutated for a joining-forms word (unlike every other RTL word, which keeps the pre-existing
  `ReplaceText`/`BidiMirrorResolver.ApplyMirroring` treatment unchanged).
- `CssLayoutEngine.MirrorWordTextIfNeeded` branches on `EffectiveJoiningForms is not null` before doing
  its ordinary text mutation - a joining-forms word only gets `MarkDisplayOrderReversed()` called instead.
- `CssBox.ResolveWordShapingFeatures` - already the single per-word override point every measure/paint
  call site shares - now also copies `DisplayOrderReversed` into a new
  `TextShapingFeatures.ReverseForDisplay` field, but only when the word actually carries joining forms.
  Always false during measurement (bidi placement, which sets `DisplayOrderReversed`, hasn't run yet by
  then) - harmless, since a shaped run's total advance is order-independent.
- `OpenTypeDescriptor.Shape` - the one glyph-walk every call site (paint, outline extraction, ToUnicode
  text extraction via `CMapInfo`) already shares - reverses the shaped `List<ShapedGlyph>` in place as its
  own final step when `ReverseForDisplay` is set, after GSUB and GPOS have both already applied against
  the true logical order. Also remaps any glyph whose whole source cluster is one `Bidi_Mirrored`
  character (`BidiMirroring.TryGetMirror`) to its own mirror-image glyph - matching what
  `BidiMirrorResolver.ApplyMirroring` already does for every other RTL word's text - though in practice
  Arabic-family letters themselves have no mirror codepoint, so this only matters for a rare embedded
  mirrorable character (a paren, say) inside a joining word.
- `CMapInfo.AddShapedText`'s glyph→source-text map is keyed by glyph *index*, not list position, so it is
  completely unaffected by whether the glyph list it walks was reversed - no change needed there.

## What was found by running it, not by reading it

- Confirmed via `Descriptor().Shape` calls in a new test that `word.Text` really does stay true logical
  order (`Lam+Alef`, not `Alef+Lam`) after this fix, and that shaping it with `ReverseForDisplay: true`
  produces exactly the reverse of the true-logical-order-with-rlig shape - i.e. rlig fires identically
  either way, and only display order differs.
- Rasterized a real two-word Arabic HTML fixture ("لا" and "بيتالف") through both PDFium and MuPDF per
  this repo's two-renderer convention: lam-alef now renders as a single connected ligature glyph in both
  renderers, replacing the previous broken "looks like a Latin U" shape.
- The same rasterization surfaced a **separate, pre-existing** defect worth its own investigation: in
  "بيتالف", Teh's two dots (produced by this font's own `ccmp` decomposition of `uni062A` into base
  `uni066E` + mark `twodotshorizontalabovear`) render at the wrong height - approximately baseline/mid-glyph
  instead of above the letter - even though the font's GPOS `mark` feature does define a valid
  `MarkBasePos` anchor for exactly `uni066E.medi` + `twodotshorizontalabovear` (confirmed directly via
  fontTools, not assumed). This reproduces identically in both PDFium and MuPDF and is unrelated to this
  fix's own reversal logic (GPOS mark-to-base positioning happens before the new reversal step, entirely
  within true-logical-order processing) - left as a distinct, not-yet-root-caused defect for separate
  follow-up, not folded into this change.

## What was deliberately not done, and why

- The Teh-dots mark-positioning defect above is not fixed here - it's a different bug (GPOS Mark-to-Base
  application, not GSUB rlig/reversal), needs its own root-cause investigation into
  `GposPositioner.ApplyMarkToBase`/`FindParticipatingPredecessor`, and bundling an unrelated fix into this
  change would make both harder to review or bisect.
- SVG text still doesn't wire `ScriptTag`/`JoiningForms` at all (pre-existing, explicitly deferred scope -
  see the original PR3 plan) - this fix only changes how an already-joining-forms-carrying HTML word
  shapes/displays, not whether SVG ever produces one.

## Evidence

- `ArabicJoiningCharacterizationTests.cs`: `EndToEndLayout_LamAlef_RligFiresViaLogicalOrderShapingThenGlyphReversal`
  (new, replaces a now-obsolete test that had documented the bug as an accepted limitation before this fix
  landed) and `EndToEndLayout_ArabicWord_ResolvesArabicScriptTagAndJoiningForms` (updated for the new
  "Text/EffectiveJoiningForms never reverse, DisplayOrderReversed does" contract) - 34/34 passing.
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`: 9644 passed, 9 pre-existing
  platform-specific skips, 0 failed (full suite, not just the Arabic/GSUB/GPOS/bidi/SVG subset).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors.
- Two-renderer (PDFium + MuPDF) rasterization of a real Arabic HTML fixture, visually inspected.
