#!/usr/bin/env python3
"""
Generates NotoSansBengaliSubset.ttf: a small subset of Google's Noto Sans Bengali
(OFL 1.1), used to verify Universal Shaping Engine (USE) syllable reordering for Bengali
(issue #533, Phase 5c) - GSUB nukt/ccmp/locl/akhn/rphf/half/rkrf/cjct/abvs/blws/pres/psts
substitution and the resulting glyph reorder (repha repositioning, pre-base matra movement),
including Bengali's own two USE categories Devanagari never reaches (GB - Consonant Placeholder,
U+0980 BENGALI ANJI; FMAbv - Syllable Modifier, U+09FE BENGALI SANDHI MARK) - against real,
readable Bengali glyphs rather than only synthetic byte-blob GSUB tables.

Keeps KA/TA/RA/SSA/HA/GA (enough consonants to form a conjunct, ক্ষ, and to exercise reph via
RA+VIRAMA), VIRAMA, the pre-base matra (vowel sign I), a post-base matra (vowel sign AA) for
contrast, NUKTA, CANDRABINDU and VISARGA (two distinct vowel-modifier positions), an independent
vowel (A), BENGALI ANJI (Consonant Placeholder, GB), BENGALI SANDHI MARK (Syllable Modifier,
FMAbv), common punctuation, digits, space, and the basic Latin alphabet (for mixed-script test
HTML) - small enough to embed as a data: URI without shipping the whole font.

Like generate_devanagari_subset.py's source, upstream Noto Sans Bengali ships only as a variable
font - this instantiates a single static (Regular weight, normal width) instance first.

Requires: fonttools.  Run:  python3 generate_bengali_subset.py path/to/NotoSansBengali-VF.ttf
Output:   NotoSansBengaliSubset.ttf (next to this script)
"""
import os
import sys

from fontTools.subset import Options, Subsetter
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

# KA(0995), TA(09A4), RA(09B0), SSA(09B7), HA(09B9), GA(0997) - consonants, enough to form the
# conjunct ক্ষ (KA+VIRAMA+SSA) and to exercise reph via RA+VIRAMA+consonant - VIRAMA(09CD),
# VOWEL SIGN I(09BF) (the pre-base matra), VOWEL SIGN AA(09BE) (a post-base matra, for contrast),
# NUKTA(09BC), CANDRABINDU(0981, above-base) and VISARGA(0983, post-base) - two distinct
# vowel-modifier positions - independent LETTER A(0985), BENGALI ANJI(0980 - Consonant
# Placeholder, this script's own GB category) and BENGALI SANDHI MARK(09FE - Syllable Modifier,
# this script's own FMAbv category) - spelled out by explicit codepoint (not typed directly) so a
# combining-mark rendering glitch in an editor can't silently substitute the wrong character - plus
# punctuation/digits/space/basic Latin.
BENGALI_CODEPOINTS = [
    0x0995, 0x09A4, 0x09B0, 0x09B7, 0x09B9, 0x0997,  # KA, TA, RA, SSA, HA, GA
    0x09CD,  # VIRAMA
    0x09BF,  # VOWEL SIGN I (pre-base matra)
    0x09BE,  # VOWEL SIGN AA (post-base matra)
    0x09BC,  # NUKTA
    0x0981,  # CANDRABINDU (vowel modifier, above-base)
    0x0983,  # VISARGA (vowel modifier, post-base)
    0x0985,  # LETTER A (independent vowel)
    0x0980,  # ANJI (Consonant Placeholder - GB)
    0x09FE,  # SANDHI MARK (Syllable Modifier - FMAbv)
]
KEEP_TEXT = "".join(chr(cp) for cp in BENGALI_CODEPOINTS) + \
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
    # (and GPOS mark attachment for the candrabindu/visarga/nukta) survive the subset undisturbed.
    options.layout_features = ["*"]
    subsetter = Subsetter(options=options)
    subsetter.populate(text=KEEP_TEXT)
    subsetter.subset(static_font)

    static_font.save(out_path)
    print(f"wrote {out_path} ({os.path.getsize(out_path)} bytes)")


if __name__ == "__main__":
    src = sys.argv[1] if len(sys.argv) > 1 else "NotoSansBengali-VF.ttf"
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(__file__), "NotoSansBengaliSubset.ttf")
    main(src, out)
