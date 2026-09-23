#!/usr/bin/env python3
"""
Generates RecursiveSubset.ttf: a small subset of ArrowType's Recursive (OFL 1.1), a real display
font with genuine OpenType stylistic-set (GSUB Alternate/Single Substitution) data - used to test/
showcase CSS Fonts Module 4's `font-variant-alternates` property and `@font-feature-values` at-rule
(specifically `styleset()`, which activates the numbered `ssNN` features this font actually has).

Recursive's static "Mono Casual" Regular instance defines `ss01` (a -> a.simple, a single-story
lowercase 'a') and `ss02` (g -> g.simple, a single-story lowercase 'g') as real GSUB Single
Substitution (Lookup Type 1) lookups - a visibly distinct glyph change, unlike a synthetic
conformance font.

Steps:
  1. Subset a basic Latin/punctuation/digit glyph set (enough for a showcase sentence), keeping every
     GSUB layout feature (so ss01/ss02/etc. and liga/case/etc. all survive).
  2. Keep glyph names (matches this repo's other subset assets, and lets a test/showcase reference a
     feature's alternate glyph by name if ever needed).

Requires: fonttools.  Run:  py -3 generate_recursive_subset.py path/to/RecursiveMonoCslSt-Regular.ttf
Output:   RecursiveSubset.ttf (next to this script)
"""
import sys
from fontTools.ttLib import TTFont
from fontTools.subset import Subsetter, Options

# Enough for a comparison-sentence showcase (before/after ss01/ss02) plus common punctuation.
KEEP_TEXT = ("ABCDEFGHIJKLMNOPQRSTUVWXYZ"
             "abcdefghijklmnopqrstuvwxyz"
             "0123456789 .,:;!?'\"()-")


def main(src_path, out_path):
    font = TTFont(src_path)

    if "fvar" in font:
        from fontTools.varLib.instancer import instantiateVariableFont
        default_location = {a.axisTag: a.defaultValue for a in font["fvar"].axes}
        instantiateVariableFont(font, default_location, inplace=True)

    options = Options()
    options.layout_features = ["*"]
    options.name_IDs = ["*"]
    options.glyph_names = True
    options.recalc_bounds = True
    subsetter = Subsetter(options=options)
    subsetter.populate(text=KEEP_TEXT)
    subsetter.subset(font)

    font.save(out_path)
    print(f"Wrote {out_path}")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print(f"Usage: {sys.argv[0]} path/to/RecursiveMonoCslSt-Regular.ttf")
        sys.exit(1)

    import os
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "RecursiveSubset.ttf")
    main(sys.argv[1], out)
