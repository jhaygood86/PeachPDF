#!/usr/bin/env python3
"""
Generates the two subsets used to show dictionary line breaking with real, readable glyphs (the `dictionary_line_breaking` showcase):

    NotoSansThaiSubset.ttf, NotoSansLaoSubset.ttf

each a subset of Google's Noto Sans for the script (SIL OFL 1.1) that keeps only the characters of the showcase's paragraphs, plus a few
punctuation marks, digits, space and the basic Latin alphabet, with every GSUB/GPOS layout feature kept so the marks and vowels shape as
they do in the full font. The upstream fonts ship as variable fonts: a single static instance (Regular weight, normal width) is made first.

Khmer and Burmese are not here: PeachPDF does not yet shape their subscript consonants and stacks, so the showcase sets those two scripts
in the line breaking test font instead.

Requires: fonttools.  Run:  python3 generate_southeast_asian_subsets.py DIR
where DIR holds the two upstream variable fonts (google/fonts, ofl/notosansthai and ofl/notosanslao), named
    NotoSansThai[wdth,wght].ttf  NotoSansLao[wdth,wght].ttf
Output: the two subsets next to this script.
"""
import os
import sys

from fontTools.subset import Options, Subsetter
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

# The paragraphs of the showcase (src/PeachPDF.TestHarness/Program.cs sets the same text).
TEXT = {
    "Thai": (
        "ประเทศไทยเป็นประเทศที่ตั้งอยู่ในเอเชียตะวันออกเฉียงใต้ กรุงเทพมหานครเป็นเมืองหลวงและเมืองที่ใหญ่ที่สุดของประเทศ "
        "ประชากรส่วนใหญ่พูดภาษาไทยและนับถือศาสนาพุทธ"
    ),
    "Lao": (
        "ປະເທດລາວມີປະຊາຊົນຫຼາຍ ນະຄອນຫຼວງວຽງຈັນເປັນເມືອງຫຼວງຂອງປະເທດລາວ ຂ້ອຍຮັກພາສາລາວ"
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
