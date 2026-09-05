#!/usr/bin/env python3
"""
Generates NotoSansGujaratiSubset.ttf: a small subset of Google's Noto Sans Gujarati
(OFL 1.1), used to verify Universal Shaping Engine (USE) syllable reordering for Gujarati
(issue #533, Phase 5c) - GSUB nukt/ccmp/locl/akhn/rphf/half/rkrf/cjct/abvs/blws/pres/psts
substitution and the resulting glyph reorder (repha repositioning, pre-base matra movement) -
against real, readable Gujarati glyphs rather than only synthetic byte-blob GSUB tables.
Gujarati needs no new UseCategory/classifier/scanner code beyond what Devanagari already
exercises (verified by enumerating every codepoint in the Gujarati block against the real UCD
data - see this feature's own recent-fixes entry), so this showcase/test asset is what actually
proves the existing pipeline shapes a second script correctly, not just Devanagari.

Keeps KA/TA/RA/SSA/HA/GA (enough consonants to form a conjunct and to exercise reph via
RA+VIRAMA), VIRAMA, the pre-base matra (vowel sign I), a post-base matra (vowel sign AA) for
contrast, NUKTA, ANUSVARA, VISARGA, an independent vowel (A), common punctuation, digits, space,
and the basic Latin alphabet (for mixed-script test HTML) - small enough to embed as a data: URI
without shipping the whole font.

Unlike generate_devanagari_subset.py's/generate_bengali_subset.py's source, upstream Noto Sans
Gujarati ships only as static weight instances (no variable font) - this subsets the Regular
instance directly, with no instantiation step needed.

Requires: fonttools.  Run:  python3 generate_gujarati_subset.py path/to/NotoSansGujarati-Regular.ttf
Output:   NotoSansGujaratiSubset.ttf (next to this script)
"""
import os
import sys

from fontTools.subset import Options, Subsetter
from fontTools.ttLib import TTFont

# KA(0A95), TA(0AA4), RA(0AB0), SSA(0AB7), HA(0AB9), GA(0A97) - consonants, enough to form a
# conjunct (KA+VIRAMA+SSA) and to exercise reph via RA+VIRAMA+consonant - VIRAMA(0ACD),
# VOWEL SIGN I(0ABF) (the pre-base matra), VOWEL SIGN AA(0ABE) (a post-base matra, for contrast),
# NUKTA(0ABC), ANUSVARA(0A82), VISARGA(0A83), independent LETTER A(0A85) - spelled out by explicit
# codepoint (not typed directly) so a combining-mark rendering glitch in an editor can't silently
# substitute the wrong character - plus punctuation/digits/space/basic Latin.
GUJARATI_CODEPOINTS = [
    0x0A95, 0x0AA4, 0x0AB0, 0x0AB7, 0x0AB9, 0x0A97,  # KA, TA, RA, SSA, HA, GA
    0x0ACD,  # VIRAMA
    0x0ABF,  # VOWEL SIGN I (pre-base matra)
    0x0ABE,  # VOWEL SIGN AA (post-base matra)
    0x0ABC,  # NUKTA
    0x0A82,  # ANUSVARA
    0x0A83,  # VISARGA
    0x0A85,  # LETTER A (independent vowel)
]
KEEP_TEXT = "".join(chr(cp) for cp in GUJARATI_CODEPOINTS) + \
    " ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.,!?:()-\"'"


def main(src_path, out_path):
    font = TTFont(src_path)

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
    subsetter.subset(font)

    font.save(out_path)
    print(f"wrote {out_path} ({os.path.getsize(out_path)} bytes)")


if __name__ == "__main__":
    src = sys.argv[1] if len(sys.argv) > 1 else "NotoSansGujarati-Regular.ttf"
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(__file__), "NotoSansGujaratiSubset.ttf")
    main(src, out)
