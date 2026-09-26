#!/usr/bin/env python3
"""
Generates LineBreakTest.ttf - the fixture for line breaking tests and the line-break showcase, where a line may end
has to be the only thing that differs between two runs.

Every character it covers is one em wide (a space is half an em), whatever it is, so a test can size a line in whole ems and
say exactly which characters fit on it, and the CJK punctuation the CSS `line-break` tailorings are about (the wave dash, the
katakana double hyphen, the middle dot, the fullwidth exclamation mark, the fullwidth percent and yen signs, the ellipsis ...)
sits in the same face as the kana and the Latin letters around it, so no character falls back to a system font (which would
make the line breaks depend on the machine and cut the text into one word per font). The glyphs are plain squares, solid for letters
and digits and hollow for punctuation, symbols and the number affixes, so a showcase can show where a line breaks around them; only their
advance widths matter to the tests.

Original, hand-authored fixture (no third-party font data), released into the public domain - see LineBreakTest.LICENSE.txt.

Regenerate with:  python3 generate_line_break_font.py
Requires: fonttools
"""
import os
import unicodedata

from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen

UPEM = 1000
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "LineBreakTest.ttf")

# The characters of the font, besides ASCII: hiragana a-o and a small i, the katakana and CJK punctuation named by
# CSS Text 3's line-break, the number affixes of East Asian width, and a few ideographs.
EXTRA = (
    "あいうえおぃ"          # hiragana a i u e o, small i
    "アイウエオー"          # katakana a i u e o, prolonged sound mark
    "々〻ゝゞヽヾ"          # iteration marks
    "〜゠・･"                      # wave dash, katakana double hyphen, middle dots
    "！？：；‼⁇⁈⁉"   # fullwidth and double exclamation and question marks, colon, semicolon
    "‥…"                                  # two dot leader, ellipsis (inseparable)
    "‐–"                                  # hyphen, en dash
    "％℃℉′″°￠"    # suffixes: fullwidth percent, degree celsius and fahrenheit, primes, degree, cent
    "＄￥￡€№±¤"    # prefixes: fullwidth dollar, yen and pound, euro, numero, plus-minus, currency
    "漢字日本語"                # ideographs
    "、。「」"                      # ideographic comma and full stop, corner brackets
)


def square(width):
    pen = TTGlyphPen(None)
    pen.moveTo((50, 0))
    pen.lineTo((50 + width, 0))
    pen.lineTo((50 + width, 700))
    pen.lineTo((50, 700))
    pen.closePath()
    return pen.glyph()


def hollow_square():
    pen = TTGlyphPen(None)
    pen.moveTo((50, 0))
    pen.lineTo((950, 0))
    pen.lineTo((950, 700))
    pen.lineTo((50, 700))
    pen.closePath()
    pen.moveTo((170, 120))
    pen.lineTo((170, 580))
    pen.lineTo((830, 580))
    pen.lineTo((830, 120))
    pen.closePath()
    return pen.glyph()


def main():
    code_points = sorted(set(range(0x21, 0x7F)) | {ord(c) for c in EXTRA})
    names = {cp: "uni%04X" % cp for cp in code_points}
    space = {0x20: "space", 0xA0: "nbspace"}
    order = [".notdef"] + list(space.values()) + [names[cp] for cp in code_points]

    fb = FontBuilder(UPEM, isTTF=True)
    fb.setupGlyphOrder(order)
    cmap = {cp: names[cp] for cp in code_points}
    cmap.update({cp: name for cp, name in space.items()})
    fb.setupCharacterMap(cmap)

    empty = TTGlyphPen(None).glyph()
    glyphs = {".notdef": square(800)}
    metrics = {".notdef": (UPEM, 50)}
    for name in space.values():
        glyphs[name] = empty
        metrics[name] = (UPEM // 2, 0)
    for cp in code_points:
        glyphs[names[cp]] = hollow_square() if unicodedata.category(chr(cp))[0] in "PSZ" else square(900)
        metrics[names[cp]] = (UPEM, 50)

    fb.setupGlyf(glyphs)
    fb.setupHorizontalMetrics(metrics)
    fb.setupHorizontalHeader(ascent=800, descent=-200)
    fb.setupNameTable({
        "familyName": "Line Break Test",
        "styleName": "Regular",
        "psName": "LineBreakTest-Regular",
        "uniqueFontIdentifier": "LineBreakTest-Regular;public-domain",
        "fullName": "Line Break Test Regular",
        "version": "Version 1.000",
    })
    fb.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200, sxHeight=500, sCapHeight=700)
    fb.setupPost()
    fb.save(OUT)
    print("wrote %s (%d bytes, %d characters)" % (OUT, os.path.getsize(OUT), len(cmap)))


if __name__ == "__main__":
    main()
