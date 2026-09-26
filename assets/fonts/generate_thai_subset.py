#!/usr/bin/env python3
"""
Generates NotoSansThaiSubset.ttf, the font that shows dictionary line breaking with real, readable glyphs (the `dictionary_line_breaking`
showcase): a subset of Google's Noto Sans Thai (SIL OFL 1.1) that keeps only the characters of the showcase's paragraph, plus a few
punctuation marks, digits, space and the basic Latin alphabet, with every GSUB/GPOS layout feature kept so the marks and vowels shape as
they do in the full font. The upstream font ships as a variable font: a single static instance (Regular weight, normal width) is made
first.

Khmer is not here: PeachPDF does not yet shape its subscript consonants, so the showcase sets it in the line breaking test font instead.

Requires: fonttools.  Run:  python3 generate_thai_subset.py DIR
where DIR holds the upstream variable font (google/fonts, ofl/notosansthai), named  NotoSansThai[wdth,wght].ttf
Output: NotoSansThaiSubset.ttf next to this script.
"""
import os
import sys

from fontTools.subset import Options, Subsetter
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

# The paragraph of the showcase (src/PeachPDF.TestHarness/Program.cs sets the same text).
TEXT = {
    "Thai": (
        "à¸›à¸£à¸°à¹€à¸—à¸¨à¹„à¸—à¸¢à¹€à¸›à¹‡à¸™à¸›à¸£à¸°à¹€à¸—à¸¨à¸—à¸µà¹ˆà¸•à¸±à¹‰à¸‡à¸­à¸¢à¸¹à¹ˆà¹ƒà¸™à¹€à¸­à¹€à¸Šà¸µà¸¢à¸•à¸°à¸§à¸±à¸™à¸­à¸­à¸à¹€à¸‰à¸µà¸¢à¸‡à¹ƒà¸•à¹‰ à¸à¸£à¸¸à¸‡à¹€à¸—à¸žà¸¡à¸«à¸²à¸™à¸„à¸£à¹€à¸›à¹‡à¸™à¹€à¸¡à¸·à¸­à¸‡à¸«à¸¥à¸§à¸‡à¹à¸¥à¸°à¹€à¸¡à¸·à¸­à¸‡à¸—à¸µà¹ˆà¹ƒà¸«à¸à¹ˆà¸—à¸µà¹ˆà¸ªà¸¸à¸”à¸‚à¸­à¸‡à¸›à¸£à¸°à¹€à¸—à¸¨ "
        "à¸›à¸£à¸°à¸Šà¸²à¸à¸£à¸ªà¹ˆà¸§à¸™à¹ƒà¸«à¸à¹ˆà¸žà¸¹à¸”à¸ à¸²à¸©à¸²à¹„à¸—à¸¢à¹à¸¥à¸°à¸™à¸±à¸šà¸–à¸·à¸­à¸¨à¸²à¸ªà¸™à¸²à¸žà¸¸à¸—à¸˜"
    ),
}

COMMON = " ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.,!?:;()-\"'"


def main(source_dir):
    here = os.path.dirname(os.path.abspath(__file__))
    for script, text in TEXT.items():
        variable_font = TTFont(os.path.join(source_dir, "NotoSans%s[wdth,wght].ttf" % script))
        static_font = instantiateVariableFont(variable_font, {"wght": 400, "wdth": 100})

        options = Options()
        options.name_IDs = ["*"]
        options.glyph_names = True
        options.recalc_bounds = True
        options.layout_features = ["*"]
        subsetter = Subsetter(options=options)
        subsetter.populate(text=text + COMMON)
        subsetter.subset(static_font)

        out = os.path.join(here, "NotoSans%sSubset.ttf" % script)
        static_font.save(out)
        print("wrote %s (%d bytes)" % (out, os.path.getsize(out)))


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else ".")
