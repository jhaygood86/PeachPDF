#!/usr/bin/env python3
"""
Generates NotoSansDevanagariSubset.ttf: a small subset of Google's Noto Sans Devanagari
(OFL 1.1), used to verify Universal Shaping Engine (USE) syllable reordering for Devanagari
(issue #533, Phase 5b) - GSUB nukt/ccmp/locl/akhn/rphf/half/rkrf/cjct/abvs/blws/pres/psts
substitution and the resulting glyph reorder (repha repositioning, pre-base matra movement) -
against real, readable Devanagari glyphs rather than only synthetic byte-blob GSUB tables.

Keeps KA/TA/RA/SSA/HA/GA (enough consonants to form a conjunct, क्ष, and to exercise reph via
RA+VIRAMA), VIRAMA, the two pre-base matras (vowel signs I and PRISHTHAMATRA E), a post-base
matra (vowel sign AA) for contrast, NUKTA, ANUSVARA, VISARGA, an independent vowel (A), common
punctuation, digits, space, and the basic Latin alphabet (for mixed-script test HTML) - small
enough to embed as a data: URI without shipping the whole font.

Like generate_arabic_subset.py's source, upstream Noto Sans Devanagari ships only as a variable
font - this instantiates a single static (Regular weight, normal width) instance first.

Requires: fonttools.  Run:  python3 generate_devanagari_subset.py path/to/NotoSansDevanagari[wdth,wght].ttf
Output:   NotoSansDevanagariSubset.ttf (next to this script)
"""
import os
import sys

from fontTools.subset import Options, Subsetter
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

# KA(0915), TA(0924), RA(0930), SSA(0937), HA(0939), GA(0917) - consonants, enough to form the
# conjunct क्ष (KA+VIRAMA+SSA) and to exercise reph via RA+VIRAMA+consonant - VIRAMA(094D),
# VOWEL SIGN I(093F)/VOWEL SIGN PRISHTHAMATRA E(094E) (the two pre-base matras),
# VOWEL SIGN AA(093E) (a post-base matra, for contrast), NUKTA(093C), ANUSVARA(0902),
# VISARGA(0903), independent LETTER A(0905) - spelled out by explicit codepoint (not typed
# directly) so a combining-mark rendering glitch in an editor can't silently substitute the wrong
# character - plus punctuation/digits/space/basic Latin.
DEVANAGARI_CODEPOINTS = [
    0x0915, 0x0924, 0x0930, 0x0937, 0x0939, 0x0917,  # KA, TA, RA, SSA, HA, GA
    0x094D,  # VIRAMA
    0x093F, 0x094E,  # VOWEL SIGN I, VOWEL SIGN PRISHTHAMATRA E (pre-base matras)
    0x093E,  # VOWEL SIGN AA (post-base matra)
    0x093C,  # NUKTA
    0x0902,  # ANUSVARA
    0x0903,  # VISARGA
    0x0905,  # LETTER A (independent vowel)
]
KEEP_TEXT = "".join(chr(cp) for cp in DEVANAGARI_CODEPOINTS) + \
    " ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.,!?:()-\"'"


def main(src_path, out_path):
    variable_font = TTFont(src_path)
    static_font = instantiateVariableFont(variable_font, {"wght": 400, "wdth": 100})

    options = Options()
    options.name_IDs = ["*"]
    options.glyph_names = True
    options.recalc_bounds = True
    # Default fontTools subsetting only keeps a curated common subset of GSUB/GPOS features -
    # explicitly keep all of them so nukt/ccmp/locl/akhn/rphf/half/rkrf/cjct/abvs/blws/pres/psts
    # (and GPOS mark attachment for the anusvara/visarga/nukta) survive the subset undisturbed.
    options.layout_features = ["*"]
    subsetter = Subsetter(options=options)
    subsetter.populate(text=KEEP_TEXT)
    subsetter.subset(static_font)

    static_font.save(out_path)
    print(f"wrote {out_path} ({os.path.getsize(out_path)} bytes)")


if __name__ == "__main__":
    src = sys.argv[1] if len(sys.argv) > 1 else "NotoSansDevanagari[wdth,wght].ttf"
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(__file__), "NotoSansDevanagariSubset.ttf")
    main(src, out)
