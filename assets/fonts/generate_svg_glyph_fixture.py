#!/usr/bin/env python3
"""Builds the SVG-in-OpenType test fixture SvgTest.ttf (CC0, see SvgTest.LICENSE.txt).

A tiny TrueType font with an `SVG ` table, one glyph per case:

* A (glyph 2)      - a plain uncompressed document with the element `glyph2`, which uses palette colours through `var(--color0, ...)`
* B, C (glyphs 3-4) - one gzip-compressed document that covers both, with the elements `glyph3` and `glyph4`; B fills with `context-fill`
* D (glyph 5)      - a single-glyph document with no `glyph5` element, which is drawn whole; it starts with a UTF-8 byte order mark
* E (glyph 6)      - a gzip bomb: a few kilobytes that inflate to more than the 4 MiB the reader allows

It also has a CPAL table so the `--colorN` variables have palette entries to stand for. The outlines (plain squares) are what a
renderer without SVG support draws. Run from anywhere: python assets/fonts/generate_svg_glyph_fixture.py. Requires fontTools.
"""
import gzip
import os

os.environ.setdefault("SOURCE_DATE_EPOCH", "1767225600")

from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import newTable
from fontTools.ttLib.tables.S_V_G_ import SVGDocument

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "SvgTest.ttf")

NS = 'xmlns="http://www.w3.org/2000/svg"'
DOC_A = (
    f'<svg {NS} viewBox="0 -1000 1000 1000">'
    '<g id="glyph2"><rect x="100" y="-800" width="800" height="800" fill="var(--color0, #ff0000)"/>'
    '<circle cx="500" cy="-400" r="250" fill="var(--color1, #0000ff)"/></g></svg>'
)
DOC_BC = (
    f'<svg {NS}>'
    '<g id="glyph3"><rect x="100" y="-800" width="800" height="800" fill="context-fill"/></g>'
    '<g id="glyph4"><path d="M100 0 L500 -800 L900 0 Z" fill="var(--color0, #00aa00)"/></g></svg>'
)
DOC_D = (
    f'<svg {NS}><rect x="100" y="-800" width="800" height="800" fill="#008080"/></svg>'
)
# 6 MiB of one character compresses to well under 10 KiB.
BOMB = f'<svg {NS}><!--'.encode() + b"a" * (6 * 1024 * 1024) + b'--></svg>'


def square():
    pen = TTGlyphPen(None)
    pen.moveTo((100, 0))
    pen.lineTo((100, 800))
    pen.lineTo((900, 800))
    pen.lineTo((900, 0))
    pen.closePath()
    return pen.glyph()


def main():
    names = [".notdef", "space", "A", "B", "C", "D", "E"]
    fb = FontBuilder(1000, isTTF=True)
    fb.setupGlyphOrder(names)
    fb.setupCharacterMap({0x20: "space", 0x41: "A", 0x42: "B", 0x43: "C", 0x44: "D", 0x45: "E"})
    empty = TTGlyphPen(None).glyph()
    glyphs = {n: (empty if n == "space" else square()) for n in names}
    fb.setupGlyf(glyphs)
    fb.setupHorizontalMetrics({n: (1000, 100) for n in names})
    fb.setupHorizontalHeader(ascent=800, descent=-200)
    fb.setupNameTable({"familyName": "SvgTest", "styleName": "Regular"})
    fb.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
    fb.setupPost()
    fb.setupCPAL([[(1.0, 0.0, 0.0, 1.0), (0.0, 0.0, 1.0, 1.0)], [(0.0, 0.6, 0.0, 1.0), (0.9, 0.9, 0.0, 1.0)]])

    svg = newTable("SVG ")
    svg.docList = [
        SVGDocument(DOC_A, 2, 2, False),
        SVGDocument(gzip.compress(DOC_BC.encode(), mtime=0), 3, 4, True),
        SVGDocument("﻿" + DOC_D, 5, 5, False),
        SVGDocument(gzip.compress(BOMB, mtime=0), 6, 6, True),
    ]
    fb.font["SVG "] = svg
    fb.save(OUT)
    print("wrote", OUT, os.path.getsize(OUT), "bytes")


if __name__ == "__main__":
    main()
