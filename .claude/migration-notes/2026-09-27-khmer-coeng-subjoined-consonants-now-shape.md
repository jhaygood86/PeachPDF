# Khmer coeng/subjoined consonants now shape

**Before:** Khmer text had no complex-script shaping at all. A subjoined ("coeng") consonant - written
in Khmer by a coeng mark (U+17D2) followed by the consonant it stacks below (or, for RO specifically,
before) its base - rendered as separate nominal glyphs in logical reading order instead of the correct
stacked/reordered form. A word like "ស្រ" (SA+COENG+RO) showed three side-by-side letter shapes rather
than RO's own pre-base presentation form sitting before SA.

**Now:** Khmer text is shaped using a port of HarfBuzz's own (pre-Universal-Shaping-Engine) Khmer
shaper: a coeng+RO pair is repositioned before the syllable's base consonant and takes the font's
pre-base (`pref`) presentation form; a coeng+any-other-consonant pair stays in place and takes the
font's below-base (`blwf`) subjoined form; a pre-base dependent vowel sign is likewise moved to the
syllable's own start. This is a rendering-correctness fix, not a new opt-in feature - any document
with Khmer text and a font that defines these OpenType features now renders it correctly automatically,
with no markup change required. A handful of Khmer vowel signs HarfBuzz's own reference shaper
decomposes at normalization time (`U+17BE`, `U+17BF`, `U+17C0`, `U+17C4`, `U+17C5`) are not decomposed
here, so a font that specifically expects the decomposed two-glyph sequence for one of those five signs
(uncommon - most fonts, including the ones this repo bundles, cover the composed codepoint directly)
still renders that one sign as its own precomposed glyph.
