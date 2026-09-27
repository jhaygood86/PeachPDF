#!/usr/bin/env python3
"""Builds the variable-font test fixture VariableCvarTest.ttf, which has a `cvar` table, and the reference values the tests compare against.

The font is synthetic and released under CC0 (see VariableCvarTest.LICENSE.txt): a rectangle glyph `H` whose hinting instructions move its top
edge to control value 0, on two axes, `wght` (100 to 900, default 400) and `wdth` (75 to 125, default 100). It is assembled with fontTools'
varLib from six masters (one at an intermediate weight, giving the `cvar` an intermediate tuple) whose `cvt` tables differ, which is what makes
varLib write a `cvar` table. The control values are:

* 0 - the height the glyph's top edge is moved to (moves with the weight)
* 1 - moves with the weight and the width
* 2 - moves with the weight only, and by more (a steeper tuple)
* 3 - negative, moves with the weight
* 4 - moves with the width only
* 5 - constant (no tuple has a delta for it, so a tuple that omits it must leave it alone)

The reference file VariableCvarTest.golden.json holds, for a grid of locations, the `cvt` table of what fontTools' own instancer
(fontTools.varLib.instancer.instantiateVariableFont) produces. The instancer rounds every control value to a whole number of font units; the
engine keeps the fraction (in 26.6), so the tests allow one unit.

Run from anywhere: python assets/fonts/generate_variable_cvar_fixture.py
Requires fontTools (any recent version).
"""
import array
import json
import os
import tempfile

# Fixed timestamps, so that running the script again writes the same bytes.
os.environ.setdefault("SOURCE_DATE_EPOCH", "1767225600")

from fontTools.designspaceLib import AxisDescriptor, DesignSpaceDocument, SourceDescriptor
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont, newTable
from fontTools.ttLib.tables import ttProgram
from fontTools.varLib import build as varlib_build
from fontTools.varLib import instancer

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_FONT = os.path.join(HERE, "VariableCvarTest.ttf")
OUT_GOLDEN = os.path.join(HERE, "VariableCvarTest.golden.json")

UPM = 1000

# (name, design weight, design width, weight factor, width factor)
MASTERS = [
    ("default", 400, 100, 1.0, 1.0),
    ("thin", 100, 100, 0.25, 1.0),
    ("semibold", 600, 100, 1.9, 1.0),     # intermediate: not linear in the weight factor
    ("black", 900, 100, 2.6, 1.0),
    ("condensed", 400, 75, 1.0, 0.75),
    ("extended", 400, 125, 1.0, 1.25),
]

LOCATIONS = [
    {"wght": 400, "wdth": 100},   # the default
    {"wght": 100, "wdth": 100},
    {"wght": 250, "wdth": 100},
    {"wght": 600, "wdth": 100},   # the intermediate master
    {"wght": 900, "wdth": 100},
    {"wght": 500, "wdth": 100},
    {"wght": 400, "wdth": 75},
    {"wght": 400, "wdth": 115},
    {"wght": 900, "wdth": 75},
    {"wght": 700, "wdth": 90},
    {"wght": 1000, "wdth": 100},  # beyond the axis: clamps to 900
]


def cvt_values(wf, wdf):
    return [
        int(700 + 20 * (wf - 1)),
        int(480 + 15 * (wf - 1) + 30 * (wdf - 1)),
        int(90 + 40 * (wf - 1)),
        int(-10 * wf),
        int(1000 * wdf),
        333,
    ]


def build_master(name, wf, wdf):
    pen = TTGlyphPen(None)
    pen.moveTo((50, 0)); pen.lineTo((50, 700)); pen.lineTo((450, 700)); pen.lineTo((450, 0)); pen.closePath()
    h = pen.glyph()

    # SVTCA[y], then MIAP[0] (no rounding and no cut-in test, so the whole control value is taken however far it is from the point) on points 1 and 2 (the top edge) with control value 0 (the arguments are pushed point first, and the last pair is
    # popped first), then IUP[y] to move the points that were not touched.
    program = ttProgram.Program()
    program.fromAssembly(["SVTCA[0]", "PUSHB[ ] 2 0 1 0", "MIAP[0]", "MIAP[0]", "IUP[0]"])
    h.program = program

    glyphs = {".notdef": TTGlyphPen(None).glyph(), "space": TTGlyphPen(None).glyph(), "H": h}
    fb = FontBuilder(UPM, isTTF=True)
    fb.setupGlyphOrder([".notdef", "space", "H"])
    fb.setupCharacterMap({0x20: "space", 0x48: "H"})
    fb.setupGlyf(glyphs)
    fb.setupHorizontalMetrics({".notdef": (500, 0), "space": (300, 0), "H": (500, 50)})
    fb.setupHorizontalHeader(ascent=800, descent=-200)
    fb.setupNameTable({"familyName": "Variable Cvar Test", "styleName": "Regular"})
    fb.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=900, usWinDescent=220, version=4, fsSelection=0x40)
    fb.setupPost()

    cvt = newTable("cvt ")
    cvt.values = array.array("h", cvt_values(wf, wdf))
    fb.font["cvt "] = cvt
    fb.font["maxp"].maxStackElements = 32
    fb.font["maxp"].maxSizeOfInstructions = 64
    return fb.font


def build_variable_font(workdir):
    doc = DesignSpaceDocument()
    weight = AxisDescriptor()
    weight.name, weight.tag = "Weight", "wght"
    weight.minimum, weight.default, weight.maximum = 100, 400, 900
    weight.labelNames = {"en": "Weight"}
    doc.addAxis(weight)
    width = AxisDescriptor()
    width.name, width.tag = "Width", "wdth"
    width.minimum, width.default, width.maximum = 75, 100, 125
    width.labelNames = {"en": "Width"}
    doc.addAxis(width)
    for name, dw, dwd, wf, wdf in MASTERS:
        path = os.path.join(workdir, "cvar-master-%s.ttf" % name)
        build_master(name, wf, wdf).save(path)
        source = SourceDescriptor()
        source.path = path
        source.name = name
        source.location = {"Weight": dw, "Width": dwd}
        if name == "default":
            source.copyInfo = True
        doc.addSource(source)
    variable, _, _ = varlib_build(doc)

    # varLib writes explicit zero deltas; leaving them out (None) makes the tuples sparse, so the reader has to read packed point numbers and
    # leave the control values a tuple names no point for untouched.
    for tuple_variation in variable["cvar"].variations:
        tuple_variation.coordinates = [c if c else None for c in tuple_variation.coordinates]
    return variable


def main():
    with tempfile.TemporaryDirectory() as workdir:
        build_variable_font(workdir).save(OUT_FONT)

    font = TTFont(OUT_FONT)
    tags = sorted(font.keys())
    assert {"cvar", "cvt ", "fvar", "gvar"} <= set(tags) or {"cvar", "cvt ", "fvar"} <= set(tags), tags

    golden = {"locations": []}
    for loc in LOCATIONS:
        instance = instancer.instantiateVariableFont(TTFont(OUT_FONT), dict(loc), inplace=False)
        golden["locations"].append({"location": loc, "cvt": list(instance["cvt "].values)})

    with open(OUT_GOLDEN, "w", encoding="utf-8", newline="\n") as f:
        json.dump(golden, f, indent=1)
        f.write("\n")
    print("tables:", " ".join(tags))
    print("wrote", OUT_FONT, os.path.getsize(OUT_FONT), "bytes and", OUT_GOLDEN)


if __name__ == "__main__":
    main()
