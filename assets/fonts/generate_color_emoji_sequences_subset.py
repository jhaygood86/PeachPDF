#!/usr/bin/env python3
"""
Generates NotoColorEmojiSequences-Subset.ttf: a small subset of Google's Noto
Color Emoji, COLR **version 1** build (OFL 1.1), covering the multi-codepoint
emoji *sequences* - the ones whose composed glyph comes from GSUB rather than
straight from the cmap:

  * a ZWJ sequence            U+1F3F3 U+FE0F U+200D U+1F308  (rainbow flag)
  * a ZWJ sequence            U+1F469 U+200D U+1F4BB         (woman technologist)
  * regional-indicator pairs  U+1F1FA U+1F1F8, U+1F1EF U+1F1F5 (US, Japan)
  * a tag sequence            U+1F3F4 U+E0067 ... U+E007F    (Scotland)
  * a bare VS16 pair          U+2764 U+FE0F                  (heart)

This is deliberately a *different* asset from NotoColorEmoji-Subset.ttf, which
covers single-codepoint color glyphs and carries no GSUB at all. Sequence
composition lives entirely in this font's `ccmp` feature - Noto Color Emoji
defines no liga/rlig/clig whatsoever - so keeping `ccmp` through subsetting is
the whole point of this script (see --layout-features below).

Note U+FE0F is intentionally NOT in the retained codepoint set: the upstream
font has no glyph for it either, and that is correct - a variation selector is a
Default_Ignorable_Code_Point that no font is expected to draw. It must render as
nothing rather than as a missing-glyph box, and the ligatures above must still
form across it.

Requires: fonttools.  Run:  python3 generate_color_emoji_sequences_subset.py path/to/NotoColorEmoji-Regular.ttf
Output:   NotoColorEmojiSequences-Subset.ttf (next to this script)
"""
import os
import sys

from fontTools.subset import Options, Subsetter
from fontTools.ttLib import TTFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "NotoColorEmojiSequences-Subset.ttf")

CODEPOINTS = [
    0x200D,                                      # ZERO WIDTH JOINER (a real glyph upstream)
    0x2764,                                      # HEAVY BLACK HEART
    0x1F308,                                     # RAINBOW
    0x1F3F3, 0x1F3F4,                            # WHITE FLAG, BLACK FLAG
    0x1F469, 0x1F4BB,                            # WOMAN, LAPTOP
    0x1F1FA, 0x1F1F8, 0x1F1EF, 0x1F1F5,          # regional indicators U, S, J, P
    # TAG LATIN letters for "gbsct" plus CANCEL TAG - the Scotland subdivision flag.
    0xE0067, 0xE0062, 0xE0073, 0xE0063, 0xE0074, 0xE007F,
]


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__.strip())

    font = TTFont(sys.argv[1])

    options = Options()
    # `ccmp` is where every sequence ligature lives; the default retained-feature
    # set would keep it, but naming it makes the dependency explicit and survives
    # a future fontTools default change.
    options.layout_features = ["ccmp"]
    options.drop_tables = []
    # COLR/CPAL are what make these color glyphs; never let them be dropped.
    options.passthrough_tables = True
    options.notdef_outline = True
    options.recalc_bounds = True

    subsetter = Subsetter(options=options)
    subsetter.populate(unicodes=CODEPOINTS)
    subsetter.subset(font)

    font.save(OUT)
    print("wrote", OUT, os.path.getsize(OUT), "bytes")


if __name__ == "__main__":
    main()
