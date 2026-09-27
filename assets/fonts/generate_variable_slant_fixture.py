#!/usr/bin/env python3
"""Builds the variable-font test fixture VariableSlantTest.ttf: a font with a weight, a width and a slant axis.

The font is synthetic and released under CC0 (see VariableSlantTest.LICENSE.txt): four glyphs made of straight strokes, on three
axes, `wght` (100 to 900, default 400), `wdth` (75 to 125, default 100) and `slnt` (-15 to 0, default 0, so a lean to the right is
negative, as the OpenType specification has it). It is what the tests of `@font-face` ranges use, because each axis changes the
glyphs in a way that is easy to read off the page and off the PDF: the stems of `I` and `H` thicken with the weight, the whole
glyph widens with the width, and every glyph is sheared with the slant.

Glyphs: I (a stem), H (two stems and a bar), A (a trapezoid with a counter), space.

Run from anywhere: python assets/fonts/generate_variable_slant_fixture.py
Requires fontTools (any recent version).
"""
import math
import os
import tempfile

# Fixed timestamps, so that running the script again writes the same bytes.
os.environ.setdefault("SOURCE_DATE_EPOCH", "1767225600")

from fontTools.designspaceLib import AxisDescriptor, DesignSpaceDocument, SourceDescriptor
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.varLib import build as varlib_build

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_FONT = os.path.join(HERE, "VariableSlantTest.ttf")

UPM = 1000
GLYPH_ORDER = [".notdef", "space", "I", "H", "A"]
CMAP = {0x20: "space", 0x49: "I", 0x48: "H", 0x41: "A"}

# (name, design weight, design width, design slant, stem thickness, width factor)
MASTERS = [
    ("default", 400, 100, 0, 100, 1.0),
    ("thin", 100, 100, 0, 40, 1.0),
    ("black", 900, 100, 0, 220, 1.0),
    ("condensed", 400, 75, 0, 100, 0.75),
    ("extended", 400, 125, 0, 100, 1.25),
    ("slanted", 400, 100, -15, 100, 1.0),
]


def build_master(stem, width, slant):
    shear = math.tan(math.radians(-slant))   # a negative slant leans to the right: x grows with y

    def pt(x, y):
        return (int(round(x * width + y * shear)), y)

    glyphs = {}

    p = TTGlyphPen(glyphs)
    p.moveTo(pt(50, 0)); p.lineTo(pt(50, 700)); p.lineTo(pt(450, 700)); p.lineTo(pt(450, 0)); p.closePath()
    glyphs[".notdef"] = p.glyph()

    glyphs["space"] = TTGlyphPen(glyphs).glyph()

    # I: one stem
    p = TTGlyphPen(glyphs)
    p.moveTo(pt(100, 0)); p.lineTo(pt(100, 700)); p.lineTo(pt(100 + stem, 700)); p.lineTo(pt(100 + stem, 0)); p.closePath()
    glyphs["I"] = p.glyph()

    # H: two stems joined by a bar
    right = 100 + 300
    p = TTGlyphPen(glyphs)
    p.moveTo(pt(100, 0)); p.lineTo(pt(100, 700)); p.lineTo(pt(100 + stem, 700)); p.lineTo(pt(100 + stem, 400 + stem // 2))
    p.lineTo(pt(right, 400 + stem // 2)); p.lineTo(pt(right, 700)); p.lineTo(pt(right + stem, 700)); p.lineTo(pt(right + stem, 0))
    p.lineTo(pt(right, 0)); p.lineTo(pt(right, 300 - stem // 2)); p.lineTo(pt(100 + stem, 300 - stem // 2)); p.lineTo(pt(100 + stem, 0))
    p.closePath()
    glyphs["H"] = p.glyph()

    # A: a trapezoid with a triangular counter
    p = TTGlyphPen(glyphs)
    p.moveTo(pt(50, 0)); p.lineTo(pt(250 - stem // 2, 700)); p.lineTo(pt(250 + stem // 2, 700)); p.lineTo(pt(450 + stem, 0))
    p.lineTo(pt(450, 0)); p.lineTo(pt(50 + stem, 0)); p.closePath()
    glyphs["A"] = p.glyph()

    fb = FontBuilder(UPM, isTTF=True)
    fb.setupGlyphOrder(GLYPH_ORDER)
    fb.setupCharacterMap(CMAP)
    fb.setupGlyf(glyphs)

    advance = {".notdef": int(500 * width), "space": int(300 * width), "I": int((200 + stem) * width),
               "H": int((600 + 2 * stem) * width), "A": int((500 + stem) * width)}
    metrics = {}
    for name in GLYPH_ORDER:
        glyph = fb.font["glyf"][name]
        glyph.recalcBounds(fb.font["glyf"])
        metrics[name] = (advance[name], getattr(glyph, "xMin", 0))
    fb.setupHorizontalMetrics(metrics)
    fb.setupHorizontalHeader(ascent=800, descent=-200, lineGap=0)
    fb.setupNameTable({"familyName": "Variable Slant Test", "styleName": "Regular"})
    fb.setupOS2(sTypoAscender=800, sTypoDescender=-200, sTypoLineGap=0, usWinAscent=850, usWinDescent=220,
                sxHeight=500, sCapHeight=700, version=4, fsSelection=0x40)
    fb.setupPost()
    return fb.font


def main():
    doc = DesignSpaceDocument()
    for name, tag, minimum, default, maximum in (("Weight", "wght", 100, 400, 900), ("Width", "wdth", 75, 100, 125),
                                                  ("Slant", "slnt", -15, 0, 0)):
        axis = AxisDescriptor()
        axis.name, axis.tag = name, tag
        axis.minimum, axis.default, axis.maximum = minimum, default, maximum
        axis.labelNames = {"en": name}
        doc.addAxis(axis)

    with tempfile.TemporaryDirectory() as workdir:
        for name, weight, width, slant, stem, factor in MASTERS:
            font = build_master(stem, factor, slant)
            path = os.path.join(workdir, "master-%s.ttf" % name)
            font.save(path)
            source = SourceDescriptor()
            source.path = path
            source.name = name
            source.location = {"Weight": weight, "Width": width, "Slant": slant}
            if name == "default":
                source.copyInfo = True
            doc.addSource(source)

        variable, _, _ = varlib_build(doc)
        variable.save(OUT_FONT)

    print("wrote", OUT_FONT, os.path.getsize(OUT_FONT), "bytes;", "tables:", " ".join(sorted(variable.keys())))


if __name__ == "__main__":
    main()
