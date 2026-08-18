#!/usr/bin/env python3
"""
Generates NotoSansJPSubset.ttf: a small subset of Google's Noto Sans JP (SIL OFL 1.1,
whose Japanese glyphs derive from Adobe's Source Han Sans - see NotoSansJPSubset.LICENSE.txt),
used to showcase real per-character CSS `text-orientation: mixed` (Unicode's Vertical_Orientation
property, UAX #50) with genuine upright CJK glyphs next to rotated Latin/digits, rather than
.notdef boxes.

Keeps only the CJK characters the showcase HTML actually uses, plus the Latin/digit/punctuation
set already covered by this repo's other showcases - small enough to embed as a data: URI without
shipping the whole (~9.5MB variable) font.

Requires: fonttools.  Run:  python3 generate_cjk_subset.py path/to/NotoSansJP[wght].ttf
Output:   NotoSansJPSubset.ttf (next to this script)
"""
import os
import sys

from fontTools.subset import Options, Subsetter
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

# The showcase's own text: "縦書きテキスト" (vertical writing text) and "PDF 2024" mixed in one
# line, plus the full basic-Latin alphabet/digits/punctuation - both for the showcase's own
# labels and so PeachPDF.Tests.Integration.TextOrientationIntegrationTests can embed this same
# font and have every character it tests (CJK and Latin alike) resolve to one face, isolating
# the Vertical_Orientation split under test from NeedsPerCodepointFont's own unrelated
# missing-glyph-fallback split.
KEEP_TEXT = ("縦書きテキスト你好"
             "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .,!?()-\"'")


def main(src_path, out_path):
    font = TTFont(src_path)

    # NotoSansJP[wght].ttf is a variable font - pin it to Regular (wght=400) before subsetting,
    # the same static-instance shape every other embedded font in this repo already is.
    if "fvar" in font:
        font = instantiateVariableFont(font, {"wght": 400})

    options = Options()
    options.name_IDs = ["*"]
    options.glyph_names = True
    options.recalc_bounds = True
    subsetter = Subsetter(options=options)
    subsetter.populate(text=KEEP_TEXT)
    subsetter.subset(font)

    font.save(out_path)
    print(f"wrote {out_path} ({os.path.getsize(out_path)} bytes)")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print("usage: generate_cjk_subset.py path/to/NotoSansJP[wght].ttf")
        sys.exit(1)

    script_dir = os.path.dirname(os.path.abspath(__file__))
    main(sys.argv[1], os.path.join(script_dir, "NotoSansJPSubset.ttf"))
