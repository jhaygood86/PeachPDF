#!/usr/bin/env python3
"""
Generates ArefRuqaaSubset.ttf: a small subset of Google's Aref Ruqaa (OFL 1.1),
used to validate GPOS Lookup Type 3 (Cursive Attachment) - GposPositioner.ApplyCursiveAttachment -
against a real font whose Arabic joining relies on it, unlike NotoSansArabicSubset.ttf (used
elsewhere for GSUB isol/init/medi/fina positional joining), which defines no `curs` GPOS feature
at all. Aref Ruqaa's own cursive baseline connections layer on top of its own positional
substitution, giving real coverage of both the RIGHT_TO_LEFT lookup-flag cascade direction and the
glyph-list-reversal-survives-cursive-attachment fix together - see this fix's own recent-fixes
entry for what this validated.

Keeps BEH/YEH/TEH/ALEF/LAM/FEH (the same letters NotoSansArabicSubset.ttf keeps, for consistency
across the Arabic-family test fixtures), common punctuation, digits, space, and the basic Latin
alphabet (for mixed-script test HTML) - small enough to embed as a data: URI without shipping the
whole font.

Unlike generate_arabic_subset.py's source, Aref Ruqaa ships as a static font already (no `fvar`
table), so no variable-font instancing step is needed here.

Requires: fonttools.  Run:  python3 generate_aref_ruqaa_subset.py path/to/ArefRuqaa-Regular.ttf
Output:   ArefRuqaaSubset.ttf (next to this script)
"""
import os
import sys

from fontTools.subset import Options, Subsetter
from fontTools.ttLib import TTFont

# Same letter set as generate_arabic_subset.py's own KEEP_TEXT, for consistency across the
# Arabic-family test fixtures - plus punctuation/digits/space/basic Latin.
KEEP_TEXT = "بيتالفح ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.,!?:()-\"'"


def main(src_path, out_path):
    font = TTFont(src_path)

    options = Options()
    options.name_IDs = ["*"]
    options.glyph_names = True
    options.recalc_bounds = True
    # Default fontTools subsetting only keeps a curated common subset of GSUB/GPOS features -
    # explicitly keep all of them so isol/init/medi/fina and, critically, `curs` (cursive
    # attachment - the whole point of this subset) survive the subset undisturbed.
    options.layout_features = ["*"]
    subsetter = Subsetter(options=options)
    subsetter.populate(text=KEEP_TEXT)
    subsetter.subset(font)

    font.save(out_path)
    print(f"wrote {out_path} ({os.path.getsize(out_path)} bytes)")


if __name__ == "__main__":
    src = sys.argv[1] if len(sys.argv) > 1 else "ArefRuqaa-Regular.ttf"
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(__file__), "ArefRuqaaSubset.ttf")
    main(src, out)
