# `font-variant-emoji` and presentation-aware font selection

Issue: `font-variant-emoji` was unsupported and U+FE0E/U+FE0F never influenced which font drew a character.

**The spec is more specific than the issue text.** CSS Fonts 4 §9.3 only affects *Emoji Presentation
Participating Code Points* - the bases in `emoji-variation-sequences.txt` (371 entries, 183 ranges) - **not**
every `Emoji=Yes` character. `#`, `*` and the digits are participants (keycap bases), so `font-variant-emoji:
emoji` really does ask for the emoji form of `7`; it only shows up when a listed font has a colour glyph
for it. The data lives in `assets/unicode/` with `generate_emoji_table.py` emitting a checked-in C# range
table (`Text/EmojiProperties.Data.g.cs`) rather than a Brotli resource: ~260 ranges, and no WASM
no-Brotli fallback to maintain.

**The load-bearing decision: the presentation is re-derived, never stored.** `CssBox.ResolveWordFont` reads a
word's first character from `OriginalText` to pick its font (see its remarks - the text can be mirrored after
measurement). The presentation of that first character is a pure function of the box's `font-variant-emoji`
and the selector right after it in the same text, so `EmojiProperties.ResolveAt(mode, text, 0)` recomputes it
identically at measure and paint time and no field was added to `CssRect`. Layout's split
(`EmitPerCodepointFragments`) resolves each character with the same `ResolveAt`, so a fragment's first
character always names the fragment's face. The selector stays glued to its base by the existing
default-ignorable rule.

**Selection order is CSS Fonts 4 §5.3** (`FontFamilyResolver.Resolve`): 1) first family whose font covers the
character *and* matches the presentation; 2) system fallback asked for a matching font
(`FontResolver.FindFamilyCoveringCodepoint(cp, presentation)`); 3) the first font that merely covers it - "ignore
the variation selector"; 4) the existing unfiltered system fallback. "Matches" is `EmojiProperties.FaceMatches`:
the face's cmap format-14 table lists the sequence, else colour-ness agrees (colour font for emoji, outline
font for text) - the colour-table heuristic the spec's note explicitly allows. A `text` request on a colour-only
stack therefore still draws colour glyphs; that is the spec's step 3, not a gap.

**`NeedsPerCodepointFont` had to learn about it**, or the single-font fast path never reaches the resolver: it now
also returns true when the box's own font does not match a requested presentation. When the box's own font
already matches, the fast path is right (pass 1 would pick that same font), so plain text and matching fonts
pay nothing.

**A base that continues into an emoji modifier, a keycap (U+20E3) or a ZWJ is left alone by the *property*** (not by an explicit selector): forcing `font-variant-emoji: text` on `👍🏽` would put the base and its skin-tone modifier in different fonts and stop the font composing the sequence. `ResolveAt` also returns before the participant lookup for `normal` with no selector, since that is nearly every character of every document.

**Caches:** the presentation is part of the `DerivedStyle._codepointFontCache` key and of the
`FontsHandler._systemFallbackFontsCache` / `FontResolver._systemFallbackCache` keys (the size and scale members
stay - see the fonts-cache invariant). It is deliberately *not* in `TextShapingFeatures`: it selects a font, not a
GSUB feature, and adding it there would fragment `GsubShaper.LookupIndexCache` for nothing.

**`font-variant-emoji` reaches shaping too** (`TextShapingFeatures.EmojiMode`, appended last and passed by name): the property is "as if" a selector followed every participant, so a font's non-default FE0E/FE0F glyph must be reachable with no selector in the text. It selects a glyph in `MapToGlyphs`, never a GSUB feature, so `GetActiveLookupIndices` ignores it.

**cmap format 14** is parsed for every selector the font lists (`CMap14`; `SyntheticUvsFont` grafts a format-14
subtable onto a bundled font for tests, since no bundled font has FE0E/FE0F records). A non-default record swaps
the dedicated glyph in inside `GsubShaper.MapToGlyphs`; the selector is still flagged and dropped last, so the
default-ignorable ordering invariant is untouched. Only U+FE0E/U+FE0F count for *font selection* - the spec
says no other variation selector may affect it - though any selector's dedicated glyph is used when shaping.
The per-face colour-ness/format-14 answer needs the face's tables, read through the same shared font-source
cache as coverage.

**Evidence:** PDFium and MuPDF rasterizations of the `font_variant_emoji` showcase agree cell for cell;
paint-level tests (`FontVariantEmojiRenderingIntegrationTests`) assert the colour path (`/ActualText` on the
colour heart) is taken exactly when emoji presentation is requested; a scripted-adapter test pins the
four-pass order without depending on installed system fonts.
