#!/usr/bin/env python3
"""
Generates the two synthetic color-glyph test fonts used by the COLR/CPAL tests
and the color-emoji showcase:

  ColorTestV0.ttf - COLR version 0 (layered outline glyphs + CPAL palette)
  ColorTestV1.ttf - COLR version 1 (paint graphs: layers, solid, linear
                    gradient, translate transform) plus v0 records

Both are original, hand-authored fixtures (no third-party font data) and are
released into the public domain (see ColorTestFonts.LICENSE.txt). Regenerate
with:  python3 generate_color_fonts.py

Requires: fonttools
"""
import os
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib.tables import otTables as ot

UPEM = 1000
HERE = os.path.dirname(os.path.abspath(__file__))

# Palette: 0=red, 1=green, 2=blue, 3=yellow (r,g,b,a floats in [0,1]).
PALETTE = [
    (1.0, 0.0, 0.0, 1.0),
    (0.0, 0.5, 0.0, 1.0),
    (0.0, 0.0, 1.0, 1.0),
    (1.0, 1.0, 0.0, 1.0),
]


def rect(pen, x0, y0, x1, y1):
    pen.moveTo((x0, y0))
    pen.lineTo((x1, y0))
    pen.lineTo((x1, y1))
    pen.lineTo((x0, y1))
    pen.closePath()


def outline_glyphs():
    glyphs = {}

    pen = TTGlyphPen(None); rect(pen, 100, 0, 900, 800); glyphs["box"] = pen.glyph()

    pen = TTGlyphPen(None); rect(pen, 200, 100, 800, 700); glyphs["circ"] = pen.glyph()

    pen = TTGlyphPen(None)
    pen.moveTo((500, 800)); pen.lineTo((100, 100)); pen.lineTo((900, 100)); pen.closePath()
    glyphs["tri"] = pen.glyph()

    # Empty glyphs (color base glyphs carry no glyf outline; space has none either).
    for name in ("colorA", "colorB", "grad", "xform", "space", ".notdef"):
        glyphs[name] = TTGlyphPen(None).glyph()

    return glyphs


def build_base(family_name):
    glyph_order = [".notdef", "space", "box", "circ", "tri", "colorA", "colorB", "grad", "xform"]
    cmap = {
        0x20: "space",
        0x58: "box",     # 'X' - a plain outline glyph in a color font (fallback path)
        0x59: "tri",     # 'Y' - layer glyph, cmapped so tests can resolve its gid
        0x5A: "circ",    # 'Z' - layer glyph
        0x41: "colorA",  # 'A'
        0x42: "colorB",  # 'B'
        0x47: "grad",    # 'G'
        0x54: "xform",   # 'T'
    }
    advances = {n: 1000 for n in glyph_order}
    advances["space"] = 500
    advances[".notdef"] = 500

    fb = FontBuilder(UPEM, isTTF=True)
    fb.setupGlyphOrder(glyph_order)
    fb.setupCharacterMap(cmap)
    fb.setupGlyf(outline_glyphs())
    metrics = {n: (advances[n], 0) for n in glyph_order}
    fb.setupHorizontalMetrics(metrics)
    fb.setupHorizontalHeader(ascent=800, descent=-200)
    fb.setupNameTable({"familyName": family_name, "styleName": "Regular"})
    fb.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
    fb.setupPost()
    return fb


def build_v0():
    fb = build_base("PeachPDF ColorTest V0")
    fb.setupCPAL([PALETTE])
    fb.setupCOLR({
        "colorA": [("box", 0), ("tri", 1)],   # red box under a green triangle
        "colorB": [("circ", 2)],              # blue circle
    })
    fb.font.save(os.path.join(HERE, "ColorTestV0.ttf"))


def build_v1():
    fb = build_base("PeachPDF ColorTest V1")
    fb.setupCPAL([PALETTE])

    glyph = lambda g, paint: {"Format": ot.PaintFormat.PaintGlyph, "Glyph": g, "Paint": paint}
    solid = lambda pal, a=1.0: {"Format": ot.PaintFormat.PaintSolid, "PaletteIndex": pal, "Alpha": a}

    colr = {
        # v1 layered solids: red box under a green triangle.
        "colorA": {
            "Format": ot.PaintFormat.PaintColrLayers,
            "Layers": [glyph("box", solid(0)), glyph("tri", solid(1))],
        },
        # v1 linear gradient clipped to the box: red -> blue left to right.
        "grad": glyph("box", {
            "Format": ot.PaintFormat.PaintLinearGradient,
            "ColorLine": {"ColorStop": [(0.0, 0), (1.0, 2)], "Extend": ot.ExtendMode.PAD},
            "x0": 100, "y0": 0, "x1": 900, "y1": 0, "x2": 100, "y2": 800,
        }),
        # v1 translate transform over a yellow triangle.
        "xform": {
            "Format": ot.PaintFormat.PaintTranslate,
            "dx": 100, "dy": 50,
            "Paint": glyph("tri", solid(3)),
        },
        # A simple single-glyph solid (blue circle).
        "colorB": glyph("circ", solid(2)),
    }
    fb.setupCOLR(colr, version=1)
    fb.font.save(os.path.join(HERE, "ColorTestV1.ttf"))


if __name__ == "__main__":
    build_v0()
    build_v1()
    print("wrote ColorTestV0.ttf and ColorTestV1.ttf")
