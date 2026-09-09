# Emoji sequences compose, and default-ignorable characters stop drawing boxes

_Landed 2026-09-10._

Three user-visible rendering changes, all in text shaping. A document author changes nothing; output that
was wrong becomes right.

**A default-ignorable character no longer draws a missing-glyph box.** Variation selectors
(`U+FE00`–`U+FE0F`, `U+E0100`–`U+E01EF`), `ZWJ`/`ZWNJ`, the bidi controls, word joiner, the Hangul fillers
and the language tag characters draw nothing when the font has no glyph for them.

- *Before:* `&#10084;&#65039;` (heart + VARIATION SELECTOR-16) rendered as a heart **followed by a tofu
  box**, because the selector fell through to the font's `.notdef` glyph.
- *After:* it renders as a plain heart.

A font that *does* define a glyph for one of these is unaffected — notably a soft hyphen (U+00AD) under
`hyphens: none` still draws whatever the font says it draws.

**Emoji sequences now compose into their single glyph.** This follows from `ccmp`/`locl` being applied to
all text (they are default-on features in the OpenType registry; PeachPDF previously reached them only for
Arabic-family and USE-shaped scripts), plus ligature matching stepping over a hidden variation selector.

- *Before:* `🇺🇸` rendered as the two regional-indicator letters "US"; `🏴󠁧󠁢󠁳󠁣󠁴󠁿` rendered as a bare black
  flag; `🏳️‍🌈` rendered as a white flag followed by a separate rainbow.
- *After:* each renders as the one composed glyph the font defines.

**A font's `ccmp`/`locl` features now apply to all text.** Beyond emoji, a font that decomposes a
precomposed letter into base + mark, or that supplies localized glyph forms, is now honored for every
script rather than only Arabic-family and USE-shaped ones. Verified as a no-change for Arabic, Devanagari
and Latin sample documents, which already reached those features through their own shaping pre-stages and
render pixel-identically.
