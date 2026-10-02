#!/usr/bin/env python3
"""
Generates ColorTestCff.otf: a synthetic COLR version 1 color font over CFF (OpenType "OTTO") outlines, for the
tests of COLR-over-CFF rendering, the Porter-Duff composite modes, the radial-gradient inner radius and the
linear-gradient p2 rotation. An original, hand-authored fixture, public domain (see ColorTestFonts.LICENSE.txt).
Regenerate with:  python3 generate_color_cff_font.py

Requires: fonttools
"""
import os
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.t2CharStringPen import T2CharStringPen
from fontTools.ttLib.tables import otTables as ot

UPEM = 1000
HERE = os.path.dirname(os.path.abspath(__file__))
PALETTE = [(1.0, 0.0, 0.0, 1.0), (0.0, 0.5, 0.0, 1.0), (0.0, 0.0, 1.0, 1.0), (1.0, 1.0, 0.0, 1.0)]


def rect(x0, y0, x1, y1):
    pen = T2CharStringPen(1000, None)
    pen.moveTo((x0, y0)); pen.lineTo((x1, y0)); pen.lineTo((x1, y1)); pen.lineTo((x0, y1)); pen.closePath()
    return pen.getCharString()


def tri():
    pen = T2CharStringPen(1000, None)
    pen.moveTo((500, 800)); pen.lineTo((100, 100)); pen.lineTo((900, 100)); pen.closePath()
    return pen.getCharString()


def empty():
    return T2CharStringPen(1000, None).getCharString()


def main():
    names = [".notdef", "space", "box", "circ", "tri", "inner", "radialR0", "radialRepeat", "linearP2",
             "srcIn", "destIn", "srcAtop", "destOver", "clear", "src", "dest", "srcOut"]
    cmap = {0x20: "space", 0x58: "box", 0x59: "tri", 0x5A: "circ", 0x49: "inner",
            0x30: "radialR0", 0x31: "radialRepeat", 0x32: "linearP2", 0x33: "srcIn", 0x34: "destIn", 0x35: "srcAtop",
            0x36: "destOver", 0x37: "clear", 0x38: "src", 0x39: "dest", 0x41: "srcOut"}
    chars = {n: empty() for n in names}
    chars["box"] = rect(100, 0, 900, 800)
    chars["circ"] = rect(200, 100, 800, 700)
    chars["inner"] = rect(350, 250, 650, 550)
    chars["tri"] = tri()

    fb = FontBuilder(UPEM, isTTF=False)
    fb.setupGlyphOrder(names)
    fb.setupCharacterMap(cmap)
    fb.setupCFF("PeachPDFColorTestCff", {"FullName": "PeachPDF ColorTest CFF"}, chars, {})
    fb.setupHorizontalMetrics({n: (500 if n in (".notdef", "space") else 1000, 0) for n in names})
    fb.setupHorizontalHeader(ascent=800, descent=-200)
    fb.setupNameTable({"familyName": "PeachPDF ColorTest CFF", "styleName": "Regular"})
    fb.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
    fb.setupPost()
    fb.setupCPAL([PALETTE])

    glyph = lambda g, paint: {"Format": ot.PaintFormat.PaintGlyph, "Glyph": g, "Paint": paint}
    solid = lambda pal, a=1.0: {"Format": ot.PaintFormat.PaintSolid, "PaletteIndex": pal, "Alpha": a}

    def composite(mode):
        # backdrop: a yellow box; source: a blue triangle
        return {"Format": ot.PaintFormat.PaintComposite, "SourcePaint": glyph("tri", solid(2)),
                "CompositeMode": mode, "BackdropPaint": glyph("box", solid(3))}

    def radial(extend, r0, r1):
        return glyph("box", {"Format": ot.PaintFormat.PaintRadialGradient,
                             "ColorLine": {"ColorStop": [(0.0, 0), (1.0, 2)], "Extend": extend},
                             "x0": 500, "y0": 400, "r0": r0, "x1": 500, "y1": 400, "r1": r1})

    colr = {
        "box": None,
        "radialR0": radial(ot.ExtendMode.PAD, 150, 400),
        "radialRepeat": radial(ot.ExtendMode.REPEAT, 0, 150),
        # p2 is not perpendicular to p0->p1: the color lines run parallel to p0->p2 (a diagonal)
        "linearP2": glyph("box", {"Format": ot.PaintFormat.PaintLinearGradient,
                                  "ColorLine": {"ColorStop": [(0.0, 0), (1.0, 2)], "Extend": ot.ExtendMode.PAD},
                                  "x0": 100, "y0": 0, "x1": 900, "y1": 0, "x2": 500, "y2": 400}),
        "srcIn": composite(ot.CompositeMode.SRC_IN),
        "destIn": composite(ot.CompositeMode.DEST_IN),
        "srcAtop": composite(ot.CompositeMode.SRC_ATOP),
        "destOver": composite(ot.CompositeMode.DEST_OVER),
        "clear": composite(ot.CompositeMode.CLEAR),
        "src": composite(ot.CompositeMode.SRC),
        "dest": composite(ot.CompositeMode.DEST),
        "srcOut": composite(ot.CompositeMode.SRC_OUT),
    }
    del colr["box"]
    fb.setupCOLR(colr, version=1)
    fb.font.save(os.path.join(HERE, "ColorTestCff.otf"))
    print("wrote ColorTestCff.otf")


if __name__ == "__main__":
    main()
