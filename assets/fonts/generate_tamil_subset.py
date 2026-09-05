#!/usr/bin/env python3
"""
Generates NotoSansTamilSubset.ttf: a small subset of Google's Noto Sans Tamil
(OFL 1.1), used to verify Universal Shaping Engine (USE) syllable reordering for Tamil
(issue #533, Phase 5c) - GSUB nukt/ccmp/locl/akhn/rphf/half/rkrf/cjct/abvs/blws/pres/psts
substitution and the resulting glyph reorder (pre-base matra movement) - against real, readable
Tamil glyphs rather than only synthetic byte-blob GSUB tables. Tamil needs no new
UseCategory/classifier/scanner code beyond what Devanagari already exercises (verified by
enumerating every codepoint in the Tamil block against the real UCD data - see this feature's own
recent-fixes entry), so this showcase/test asset is what actually proves the existing pipeline
shapes a third script correctly, not just Devanagari.

Keeps KA/TA/RA/SSA/HA (SSA and HA are Grantha-origin letters Tamil borrows for Sanskrit loanwords,
kept specifically so a font's own க்ஷ conjunct ligature - KA+VIRAMA+SSA - has a chance to exercise
cjct/half the same way Devanagari's Sanskrit-loanword conjunct does), VIRAMA (pulli), the pre-base
matra (vowel sign E), a post-base matra (vowel sign AA) for contrast, ANUSVARA, AAYTHAM (Tamil's
own Modifying_Letter - classifies as USE category O, not a vowel modifier, unlike Devanagari's/
Bengali's/Gujarati's Visarga - see UseCategoryClassifierTests' own TamilVisarga_FallsBackToOther),
an independent vowel (A), common punctuation, digits, space, and the basic Latin alphabet (for
mixed-script test HTML) - small enough to embed as a data: URI without shipping the whole font.

Like generate_devanagari_subset.py's/generate_bengali_subset.py's source, upstream Noto Sans Tamil
ships only as a variable font - this instantiates a single static (Regular weight, normal width)
instance first.

Requires: fonttools.  Run:  python3 generate_tamil_subset.py path/to/NotoSansTamil-VF.ttf
Output:   NotoSansTamilSubset.ttf (next to this script)
"""
import os
import sys

from fontTools.subset import Options, Subsetter
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

# KA(0B95), TA(0BA4), RA(0BB0), SSA(0BB7), HA(0BB9) - consonants, enough to try the Grantha-origin
# க்ஷ conjunct (KA+VIRAMA+SSA) - VIRAMA(0BCD, pulli), VOWEL SIGN E(0BC6) (the pre-base matra),
# VOWEL SIGN AA(0BBE) (a post-base matra, for contrast), ANUSVARA(0B82), AAYTHAM(0B83 - Tamil's own
# Modifying_Letter, USE category O), independent LETTER A(0B85) - spelled out by explicit codepoint
# (not typed directly) so a combining-mark rendering glitch in an editor can't silently substitute
# the wrong character - plus punctuation/digits/space/basic Latin.
TAMIL_CODEPOINTS = [
    0x0B95, 0x0BA4, 0x0BB0, 0x0BB7, 0x0BB9,  # KA, TA, RA, SSA, HA
    0x0BCD,  # VIRAMA (pulli)
    0x0BC6,  # VOWEL SIGN E (pre-base matra)
    0x0BBE,  # VOWEL SIGN AA (post-base matra)
    0x0B82,  # ANUSVARA
    0x0B83,  # AAYTHAM (Modifying_Letter - USE category O)
    0x0B85,  # LETTER A (independent vowel)
]
KEEP_TEXT = "".join(chr(cp) for cp in TAMIL_CODEPOINTS) + \
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
    # (and GPOS mark attachment for the anusvara) survive the subset undisturbed.
    options.layout_features = ["*"]
    subsetter = Subsetter(options=options)
    subsetter.populate(text=KEEP_TEXT)
    subsetter.subset(static_font)

    static_font.save(out_path)
    print(f"wrote {out_path} ({os.path.getsize(out_path)} bytes)")


if __name__ == "__main__":
    src = sys.argv[1] if len(sys.argv) > 1 else "NotoSansTamil-VF.ttf"
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(__file__), "NotoSansTamilSubset.ttf")
    main(src, out)
