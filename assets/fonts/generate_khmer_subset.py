#!/usr/bin/env python3
"""
Generates NotoSansKhmerSubset.ttf: a small subset of Google's Noto Sans Khmer
(OFL 1.1), used to verify Khmer's coeng/subjoined-consonant shaping (issue #1493) - GSUB
locl/ccmp/pref/blwf/abvf/pstf/pres/abvs/blws/psts substitution and the resulting glyph reorder
(coeng+RO repositioning before the base, pre-base vowel-sign movement) - against real, readable
Khmer glyphs rather than only synthetic byte-blob GSUB tables.

Keeps KA/RO/SA/NO/MO plus the consonants the showcase's own sentence needs (enough consonants to
exercise both `pref` - via COENG+RO - and `blwf` - via COENG+a non-RO consonant), COENG, the
dependent vowel signs both the tests and the showcase sentence need (one per reorder-relevant
position: E/AE for VPre, I for VAbv, U for VBlw, AA for VPst - each with a "clean" Unicode canonical
form, avoiding the five split matras HarfBuzz's own Khmer shaper decomposes at normalization time,
which this port does not implement - see `.claude/accepted-gaps/no-text-shaping.md`), an independent
vowel (QA), the Xgroup/Ygroup/Robatic signs the showcase sentence and tests need, KHAN
(punctuation), two digits, common punctuation, and the basic Latin alphabet (for mixed-script test
HTML) - small enough to embed as a data: URI without shipping the whole font.

Like generate_bengali_subset.py's source, upstream Noto Sans Khmer ships only as a variable font -
this instantiates a single static (Regular weight, normal width) instance first.

Requires: fonttools.  Run:  python3 generate_khmer_subset.py path/to/NotoSansKhmer-VF.ttf
Output:   NotoSansKhmerSubset.ttf (next to this script)
"""
import os
import sys

from fontTools.subset import Options, Subsetter
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

# KA(1780), RO(179A - Ra), SA(179F), NO(1793), MO(1798) - consonants, enough to exercise both a
# `pref`-substituted COENG+RO pair and a `blwf`-substituted COENG+other-consonant pair - plus the
# further consonants (KHA 1781, NYO 1789, TTHO 178E, PHA 1797, QA-as-independent-vowel-base 17A1)
# the showcase's own sentence (below) needs. COENG (17D2), VOWEL SIGN E(17C1)/AE(17C2, pre-base/
# VPre), VOWEL SIGN I(17B7, above-base/VAbv), VOWEL SIGN U(17BB, below-base/VBlw), VOWEL SIGN
# AA(17B6, post-base/VPst) - one dependent vowel sign per reorder-relevant position, all with a
# "clean" Unicode canonical form (avoiding the five split matras HarfBuzz's own Khmer shaper
# decomposes at normalization time, which this port does not implement - see
# `.claude/accepted-gaps/no-text-shaping.md`) - INDEPENDENT VOWEL QA(17A2), SIGN NIKAHIT(17C6 -
# Xgroup), SIGN BANTOC(17CB - Xgroup), SIGN TOANDAKHIAT(17CD - Xgroup), SIGN REAHMUK(17C7 -
# Ygroup), SIGN ROBAT(17CC - Robatic), KHAN(17D4, punctuation) and two digits - spelled out by
# explicit codepoint (not typed directly) so a combining-mark rendering glitch in an editor can't
# silently substitute the wrong character - plus punctuation/digits/space/basic Latin.
KHMER_CODEPOINTS = [
    0x1780, 0x1781, 0x1789, 0x178E, 0x1793, 0x1797, 0x1798, 0x179A, 0x179F,  # consonants
    0x17A1, 0x17A2,  # independent vowels QAA, QA
    0x17D2,  # COENG
    0x17C1,  # VOWEL SIGN E (VPre)
    0x17C2,  # VOWEL SIGN AE (VPre)
    0x17B7,  # VOWEL SIGN I (VAbv)
    0x17BB,  # VOWEL SIGN U (VBlw)
    0x17B6,  # VOWEL SIGN AA (VPst)
    0x17C6,  # SIGN NIKAHIT (Xgroup)
    0x17CB,  # SIGN BANTOC (Xgroup)
    0x17CD,  # SIGN TOANDAKHIAT (Xgroup)
    0x17C7,  # SIGN REAHMUK (Ygroup)
    0x17CC,  # SIGN ROBAT (Robatic)
    0x17D4,  # KHAN
    0x17E0, 0x17E1,  # digits 0, 1
]

# The showcase's own sentence (src/PeachPDF.TestHarness/Program.cs, "dictionary_line_breaking"
# showcase's Khmer block) - "ខ្ញុំស្រឡាញ់ភាសាខ្មែរណាស់" ("I love the Khmer language very much"),
# chosen to exercise both a coeng+RO pair (ស្រ) and two coeng+other-consonant pairs (ខ្ញ, ខ្ម)
# while avoiding the five split-matra codepoints above.
SHOWCASE_TEXT = "ខ្ញុំស្រឡាញ់ភាសាខ្មែរណាស់"
KEEP_TEXT = "".join(chr(cp) for cp in KHMER_CODEPOINTS) + SHOWCASE_TEXT + \
    " ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.,!?:()-\"'"


def main(src_path, out_path):
    variable_font = TTFont(src_path)
    static_font = instantiateVariableFont(variable_font, {"wght": 400, "wdth": 100})

    options = Options()
    options.name_IDs = ["*"]
    options.glyph_names = True
    options.recalc_bounds = True
    # Default fontTools subsetting only keeps a curated common subset of GSUB/GPOS features -
    # explicitly keep all of them so locl/ccmp/pref/blwf/abvf/pstf/pres/abvs/blws/psts survive the
    # subset undisturbed.
    options.layout_features = ["*"]
    subsetter = Subsetter(options=options)
    subsetter.populate(text=KEEP_TEXT)
    subsetter.subset(static_font)

    static_font.save(out_path)
    print(f"wrote {out_path} ({os.path.getsize(out_path)} bytes)")


if __name__ == "__main__":
    src = sys.argv[1] if len(sys.argv) > 1 else "NotoSansKhmer-VF.ttf"
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(__file__), "NotoSansKhmerSubset.ttf")
    main(src, out)
