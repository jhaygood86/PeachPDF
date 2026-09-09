# `ccmp` is default-on, and a default-ignorable is never drawn

_Landed 2026-09-10._

Three separate defects, found by rendering `@font-face`-loaded **real COLR v1 Noto Color Emoji** and
looking at the output. They compose: only the third one is what most emoji actually needed, and it was
invisible behind the first two.

**1. A `Default_Ignorable_Code_Point` fell through to `.notdef`.** `&#10084;&#65039;` (heart +
VARIATION SELECTOR-16) drew a heart **followed by a tofu box**, in both PDFium and MuPDF. The font
correctly has no cmap entry for U+FE0F — that is the whole point of the property — so it mapped to glyph
0 and got painted. New `Text/UnicodeDefaultIgnorables.cs` (17 UCD ranges, inline comparison chain — far
too small to be worth the Brotli generated-table pattern `VerticalOrientationTable`/`BidiClassTable` use).
`GsubShaper.MapToGlyphs` flags such a glyph as `ShapedGlyph.IsHiddenIgnorable` at map time, where the
codepoint and its glyph are both already in hand, and `OpenTypeDescriptor.DropHiddenIgnorables` deletes
them at the very end of `Shape`.

The ordering is load-bearing and is the trap here: **flag early, delete last.** An ignorable is
load-bearing *during* shaping (ZWJ is exactly what makes an emoji ZWJ sequence ligate; a bidi control can
be the context a contextual rule matches), so it has to reach every lookup and only then be hidden —
HarfBuzz's `hide_default_ignorables` sequences it the same way. Only glyph index 0 is ever flagged, so a
font that ships a real blank glyph for an ignorable is honored as authored; that is also what keeps a soft
hyphen (U+00AD is itself default-ignorable, and most fonts draw it as a visible hyphen when `hyphens: none`
leaves it in the text) behaving exactly as before. Deletion remaps `AttachedToIndex`, which is a
*glyph-list* index; `LigatureComponentClusterStarts` holds text offsets and needs no fixup.

**2. A hidden ignorable broke ligature component matching.** `U+1F3F3 U+FE0F U+200D U+1F308` did not form
the rainbow-flag glyph, because the font's ligature reads flag + ZWJ + rainbow and never mentions the
variation selector — the unmapped FE0F sat between the components and broke the run.
`GsubShaper.TryMatchLigature` now steps over an `IsHiddenIgnorable` glyph instead of matching against it
(HarfBuzz's `SKIP_MAYBE`), carrying it in the existing `skipped` list so it is re-inserted after the merge
and then deleted with every other hidden ignorable. Safe by construction: a component glyph id is a real
glyph, never `.notdef`, so this can only turn a previous *failure* into a match.

**3. `ccmp`/`locl` were never applied to ordinary text — the big one.** They were reachable *only* from the
Arabic joining pre-stage and the USE pre-stage, so any text that is neither got no `ccmp` at all. Both are
default-on in the OpenType feature registry, not opt-ins. This is not theoretical: **Noto Color Emoji's
entire `GSUB` FeatureList is a single `ccmp` record**, so 🇺🇸 rendered as the letters "US", and
🏴󠁧󠁢󠁳󠁣󠁴󠁿 as a bare black flag.

The trap worth remembering: **the rainbow flag worked, and it worked by accident.** ZWJ (U+200D) has
`Joining_Type=Join_Causing`, so an emoji ZWJ sequence produced a non-empty `JoiningForms` array, tripped
the *Arabic* pre-stage, and picked up `ccmp` as a side effect. That one working case is exactly why the
gap survived — it made emoji look supported. It also cost real debugging time here: reasoning from the
code said the ligature could not form, while the rendered PDF plainly showed that it did. The instrumented
run (`join=4` on a six-UTF-16-unit emoji run) is what settled it. Read the render, not the code.

`GetActiveLookupIndices` now adds `ccmp`/`locl` to `defaultTags`, **gated on neither pre-stage having
run** (`JoiningForms` and `UseCategories` both empty). That gate is not tidiness: a second application is
not idempotent in general — a Type 2 (Multiple Substitution) decomposition will happily decompose its own
output again.

**Evidence.** All three flag families correct in **both PDFium and MuPDF** (ZWJ, regional-indicator, and
tag sequences — Scotland and Wales included). Arabic, Devanagari and Latin render **pixel-identical**
before/after (`ImageChops.difference` bbox `None`), which is what proves the gate holds and bounds the
blast radius of #3 to text that previously got no `ccmp` at all. Suite: 10482 passing; the single failure
(`FontSynthesisIntegrationTests.ObliqueWithExplicitAngle_…`) was verified to fail identically on a clean
tree and is unrelated. New `DefaultIgnorableShapingTests` (30 cases) fails **21/30** with the library
changes reverted, so it is not a no-op guard.

**Deliberately not done.** cmap **format-14** variation sequences are still unapplied — see
[per-character-font-matching-boundaries.md](../accepted-gaps/per-character-font-matching-boundaries.md).
It was considered first and measured: it would have fixed nothing here. All 371 `U+FE0F` records in the
real Noto Color Emoji are *default* UVS records (glyph `None`), and `U+1F600 U+FE0F` is absent from its
UVS table entirely, so the tofu would have survived. Emoji-sequence composition comes from `GSUB`, not the
cmap. Nor was the `SKIP_MAYBE` treatment extended to *contextual* (GSUB 5/6) and GPOS matching — only
ligature component matching needed it for these fonts, and the contextual matchers reach into Arabic/Indic
paths this repo has repeatedly had to repair; that is a separate change with its own verification burden.

Fixture: `assets/fonts/CcmpLigatureTest.ttf` (hand-authored, CC0, `generate_ccmp_ligature_font.py`) — its
only GSUB feature is `ccmp` and it has no U+FE0F glyph, so a regression in either #1 or #3 fails it
immediately. The bundled `NotoColorEmoji-Subset.ttf` could not be used: it has **no GSUB features at all**.
