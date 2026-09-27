#!/usr/bin/env python3
"""
Generates NotoSansLaoSubset.ttf, the font that shows dictionary line breaking with real, readable glyphs (the `dictionary_line_breaking`
showcase): a subset of Google's Noto Sans Lao (SIL OFL 1.1) that keeps only the characters of the showcase's paragraph, plus a few
punctuation marks, digits, space and the basic Latin alphabet, with every GSUB/GPOS layout feature kept so the marks and tone signs
shape as they do in the full font. The upstream font ships as a variable font: a single static instance (Regular weight, normal width)
is made first.

Requires: fonttools.  Run:  python3 generate_lao_subset.py DIR
where DIR holds the upstream variable font (google/fonts, ofl/notosanslao), named  NotoSansLao[wdth,wght].ttf
Output: NotoSansLaoSubset.ttf next to this script.
"""
import os
import sys

from fontTools.subset import Options, Subsetter
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

# The paragraph of the showcase (src/PeachPDF.TestHarness/Program.cs sets the same text), built from word groups already
# exercised by the dictionary line-breaking tests (PeachDrawing.Text.Tests/Text/Segmentation/DictionaryLineBreakingTests.cs).
TEXT = {
    "Lao": (
        "ປະເທດລາວຕັ້ງຢູ່ໃນເອເຊຍຕາເວັນອອກສຽງໃຕ້ "
        "ນະຄອນຫຼວງວຽງຈັນເປັນເມືອງຫຼວງຂອງປະເທດລາວ "
        "ຂ້ອຍຮັກພາສາລາວ"
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
