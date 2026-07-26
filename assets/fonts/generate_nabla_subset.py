#!/usr/bin/env python3
"""
Generates NablaSubset.ttf: a small subset of Google's Nabla (OFL 1.1), a COLR v1
color font with 7 CPAL palettes, used to test/showcase the CSS `font-palette`
property and `@font-palette-values` at-rule.

Steps:
  1. Instantiate the variable font at its default axis location (-> static, smaller,
     and matches how PeachPDF renders color glyphs: at the default instance).
  2. Subset to a handful of glyphs (the letters in "PALETTE") keeping COLR/CPAL.
  3. Upgrade CPAL to v1 and flag two palettes with the light/dark background bits,
     so `font-palette: light`/`dark` (which read the CPAL palette-type flags) have
     something to select. This is the only change to Nabla's own palette data.

Requires: fonttools, brotli.  Run:  py -3 generate_nabla_subset.py path/to/Nabla.ttf
Output:   NablaSubset.ttf (next to this script)
"""
import sys
import os
from fontTools.ttLib import TTFont
from fontTools.subset import Subsetter, Options
from fontTools.varLib.instancer import instantiateVariableFont

KEEP_TEXT = "PALETTE"  # decorative uppercase letters
# CPAL palette-type bits (OpenType CPAL v1).
USABLE_WITH_LIGHT_BACKGROUND = 0x0001
USABLE_WITH_DARK_BACKGROUND = 0x0002


def main(src_path, out_path):
    font = TTFont(src_path)

    if "fvar" in font:
        default_location = {a.axisTag: a.defaultValue for a in font["fvar"].axes}
        instantiateVariableFont(font, default_location, inplace=True)

    # PeachPDF renders color glyphs from COLR/CPAL over glyf, not SVG-in-OpenType; drop the SVG table
    # (also avoids the lxml dependency the subsetter needs to subset it).
    if "SVG " in font:
        del font["SVG "]

    options = Options()
    options.layout_features = ["*"]
    options.name_IDs = ["*"]
    options.glyph_names = True
    options.recalc_bounds = True
    options.drop_tables = []  # keep COLR/CPAL
    subsetter = Subsetter(options=options)
    subsetter.populate(text=KEEP_TEXT)
    subsetter.subset(font)

    # Upgrade CPAL to v1 and flag two palettes so light/dark keywords resolve to a real palette.
    cpal = font["CPAL"]
    num = len(cpal.palettes)
    types = [0] * num
    if num > 1:
        types[1] = USABLE_WITH_DARK_BACKGROUND
    if num > 2:
        types[2] = USABLE_WITH_LIGHT_BACKGROUND
    cpal.version = 1
    cpal.paletteTypes = types
    cpal.paletteLabels = [0xFFFF] * num
    cpal.paletteEntryLabels = [0xFFFF] * cpal.numPaletteEntries

    font.save(out_path)
    print(f"wrote {out_path} ({os.path.getsize(out_path)} bytes); "
          f"{num} palettes x {cpal.numPaletteEntries} entries; types={types}")


if __name__ == "__main__":
    src = sys.argv[1] if len(sys.argv) > 1 else "Nabla.ttf"
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(__file__), "NablaSubset.ttf")
    main(src, out)
