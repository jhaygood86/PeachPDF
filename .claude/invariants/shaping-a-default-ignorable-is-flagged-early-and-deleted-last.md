# A default-ignorable is flagged early and deleted last

A codepoint with Unicode's `Default_Ignorable_Code_Point` property that the font has no glyph for is
identified in `GsubShaper.MapToGlyphs` (flagged `ShapedGlyph.IsHiddenIgnorable`) and removed only at the
very end of `OpenTypeDescriptor.Shape`, after **both** GSUB and GPOS. Neither end of that span is
arbitrary.

**It cannot be removed earlier**, because an ignorable is load-bearing *during* shaping. ZWJ (U+200D) is
precisely what makes an emoji ZWJ sequence ligate — a font's rule reads flag + ZWJ + rainbow, with the ZWJ
as a real component — and a bidi control can be the context a contextual rule matches on. Delete them
before the lookups and those substitutions silently stop happening. Real shaping engines sequence it the
same way (HarfBuzz's `hide_default_ignorables` runs last).

**It cannot be decided later**, because after GSUB a glyph's cluster no longer reliably maps back to one
source codepoint: a ligature merge rewrites cluster spans, so re-deriving "was this .notdef standing in for
a single ignorable codepoint?" from `(text, ClusterStart, ClusterLength)` after the fact is guesswork. Map
time is the one place the codepoint and its resolved glyph are both in hand.

**Only glyph index 0 is ever flagged.** A font that ships a real (blank, zero-advance) glyph for an
ignorable is honored as authored rather than second-guessed. This is also what keeps a soft hyphen
(U+00AD — itself default-ignorable, and drawn as a visible hyphen by most fonts when `hyphens: none`
leaves it in the text) rendering as it always did. A future "simplification" that drops every
default-ignorable regardless of glyph coverage will start swallowing visible soft hyphens.

**Deletion must remap `ShapedGlyph.AttachedToIndex`** — it is a *glyph-list* index, so removing entries
shifts it. A mark attached to a deleted glyph loses its anchor (`null`) rather than silently pointing at
whatever slid into that slot. `LigatureComponentClusterStarts` holds *text* offsets, not glyph indices, and
must not be "fixed up" to match.

Measured symptom if the ordering is broken: at the early end, `🏳️‍🌈` renders as a white flag followed by a
separate rainbow instead of one flag glyph. At the "never remove it" end, `&#10084;&#65039;` renders as a
heart followed by a tofu box, in both PDFium and MuPDF.

See [`.claude/recent-fixes/2026-09-10-ccmp-is-default-on-and-default-ignorables-are-never-drawn.md`](../recent-fixes/2026-09-10-ccmp-is-default-on-and-default-ignorables-are-never-drawn.md).
