#!/usr/bin/env python3
"""
Generates NotoSansHebrewSubset.ttf: a small subset of Google's Noto Sans Hebrew
(OFL 1.1), used to showcase the HTML `dir` attribute / `<bdo>`/`<bdi>` / CSS
`direction`/`unicode-bidi` / the Unicode Bidi Algorithm with real, readable
Hebrew (script class strong-R) glyphs rather than .notdef boxes.

Keeps the full Hebrew alphabet (including final forms) plus the punctuation,
digits and space the showcase HTML actually uses - small enough to embed as a
data: URI without shipping the whole font.

Requires: fonttools.  Run:  python3 generate_hebrew_subset.py path/to/NotoSansHebrew-Regular.ttf
Output:   NotoSansHebrewSubset.ttf (next to this script)
"""
import os
import sys

from fontTools.subset import Options, Subsetter
from fontTools.ttLib import TTFont

# Full Hebrew alphabet (aleph-tav) plus the five final forms, and the
# punctuation/digits/space the showcase HTML needs.
KEEP_TEXT = "אבגדהוזחטיכלמנסעפצקרשתךםןףץ 0123456789.,!?:()-\"'"


def main(src_path, out_path):
    font = TTFont(src_path)

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
    src = sys.argv[1] if len(sys.argv) > 1 else "NotoSansHebrew-Regular.ttf"
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(__file__), "NotoSansHebrewSubset.ttf")
    main(src, out)
