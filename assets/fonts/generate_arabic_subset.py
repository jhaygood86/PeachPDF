#!/usr/bin/env python3
"""
Generates NotoSansArabicSubset.ttf: a small subset of Google's Noto Sans Arabic
(OFL 1.1), used to verify Arabic-family cursive joining (issue #533) - GSUB
isol/init/medi/fina positional substitution and rlig lam-alef ligature
formation - against real, readable Arabic glyphs rather than only synthetic
byte-blob GSUB tables.

Keeps BEH/YEH/TEH/ALEF/LAM/FEH (enough to exercise Dual-joining letters, a
Right-joining letter, and the lam-alef ligature), common punctuation, digits,
space, and the basic Latin alphabet (for mixed-script test HTML) - small
enough to embed as a data: URI without shipping the whole font.

Unlike generate_hebrew_subset.py's source, upstream Noto Sans Arabic ships
only as a variable font - this instantiates a single static (Regular weight,
normal width) instance first, since subsetting a variable font's GSUB/GVAR
data correctly is a materially different (and less thoroughly exercised)
fontTools code path than subsetting a static font.

Requires: fonttools.  Run:  python3 generate_arabic_subset.py path/to/NotoSansArabic[wdth,wght].ttf
Output:   NotoSansArabicSubset.ttf (next to this script)
"""
import os
import sys

from fontTools.subset import Options, Subsetter
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

# BEH/YEH/TEH (Dual-joining), ALEF (Right-joining), LAM (Dual-joining, for the
# lam-alef ligature), FEH (Dual-joining) - enough Arabic letters to exercise
# every joining-type column real running text needs - plus punctuation/digits/
# space/basic Latin.
KEEP_TEXT = "بيتالفح ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.,!?:()-\"'"


def main(src_path, out_path):
    variable_font = TTFont(src_path)
    static_font = instantiateVariableFont(variable_font, {"wght": 400, "wdth": 100})

    options = Options()
    options.name_IDs = ["*"]
    options.glyph_names = True
    options.recalc_bounds = True
    # Default fontTools subsetting only keeps a curated common subset of GSUB/
    # GPOS features - explicitly keep all of them so isol/init/medi/fina/rlig
    # (and GPOS mark attachment for the digits' vowel-diacritic-adjacent forms,
    # if any) survive the subset undisturbed.
    options.layout_features = ["*"]
    subsetter = Subsetter(options=options)
    subsetter.populate(text=KEEP_TEXT)
    subsetter.subset(static_font)

    static_font.save(out_path)
    print(f"wrote {out_path} ({os.path.getsize(out_path)} bytes)")


if __name__ == "__main__":
    src = sys.argv[1] if len(sys.argv) > 1 else "NotoSansArabic[wdth,wght].ttf"
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(__file__), "NotoSansArabicSubset.ttf")
    main(src, out)
