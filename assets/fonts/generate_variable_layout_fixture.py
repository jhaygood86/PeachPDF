#!/usr/bin/env python3
"""Builds VariableLayoutTest.ttf and VariableLayoutTest.golden.json (CC0, see VariableLayoutTest.LICENSE.txt).

A tiny variable TrueType font (one `wght` axis, 100 to 900, default 400) whose *positioning* varies, to test that an instance of a
variable font is shaped with its `GPOS` deltas: the `VariationIndex` device tables of value records and anchors, and the item
variation store in `GDEF`. It is assembled by fontTools' varLib from three masters, each with a `GPOS` written in feature syntax:

* `kern`: pair positioning A V and V A (a value record whose x advance varies) and a single adjustment on W (advance and placement)
* `mark`: a mark-to-base attachment of `acute` to A whose base anchor moves with the weight (format 3 anchors)

The glyphs are plain rectangles: only the numbers matter. The golden file holds, for a grid of weights, what fontTools' own instancer
(`instantiateVariableFont`) leaves in the `GPOS` of the instance: the kern values of the two pairs, the single adjustment and the
base anchor, all rounded as fontTools rounds them. Run: python assets/fonts/generate_variable_layout_fixture.py. Requires fontTools.
"""
import json
import os
import tempfile

os.environ.setdefault("SOURCE_DATE_EPOCH", "1767225600")

from fontTools.designspaceLib import AxisDescriptor, DesignSpaceDocument, SourceDescriptor
from fontTools.feaLib.builder import addOpenTypeFeaturesFromString
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont
from fontTools.varLib import build as varlib_build
from fontTools.varLib import instancer

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_FONT = os.path.join(HERE, "VariableLayoutTest.ttf")
OUT_GOLDEN = os.path.join(HERE, "VariableLayoutTest.golden.json")

GLYPHS = [".notdef", "space", "A", "V", "W", "acute"]
CMAP = {0x20: "space", 0x41: "A", 0x56: "V", 0x57: "W", 0xB4: "acute"}

# name, design weight, kern A V, kern V A, W (x placement, x advance), base anchor x, base anchor y
MASTERS = [
    ("default", 400, -50, -80, (10, 30), 250, 700),
    ("thin", 100, -20, -30, (4, 12), 200, 690),
    ("black", 900, -110, -190, (25, 75), 330, 730),
]
LOCATIONS = [100, 250, 400, 500, 650, 900]


def square(glyphs, width):
    pen = TTGlyphPen(glyphs)
    pen.moveTo((50, 0)); pen.lineTo((50, 700)); pen.lineTo((width - 50, 700)); pen.lineTo((width - 50, 0)); pen.closePath()
    return pen.glyph()


def master(name, kav, kva, w_values, anchor_x, anchor_y):
    glyphs = {}
    glyphs[".notdef"] = square(glyphs, 500)
    glyphs["space"] = TTGlyphPen(glyphs).glyph()
    for g in ("A", "V", "W"):
        glyphs[g] = square(glyphs, 600)
    glyphs["acute"] = square(glyphs, 200)

    fb = FontBuilder(1000, isTTF=True)
    fb.setupGlyphOrder(GLYPHS)
    fb.setupCharacterMap(CMAP)
    fb.setupGlyf(glyphs)
    fb.setupHorizontalMetrics({g: (600 if g in ("A", "V", "W") else 500 if g == ".notdef" else 250 if g == "space" else 200, 50) for g in GLYPHS})
    fb.setupHorizontalHeader(ascent=800, descent=-200)
    fb.setupNameTable({"familyName": "Variable Layout Test", "styleName": "Regular"})
    fb.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200, version=4)
    fb.setupPost()

    feature = """
languagesystem DFLT dflt;
languagesystem latn dflt;
table GDEF { GlyphClassDef [A V W], , [acute], ; } GDEF;
feature kern {
    pos A V %d;
    pos V A %d;
    pos W <%d 0 %d 0>;
} kern;
markClass acute <anchor 0 600> @ACCENT;
feature mark {
    pos base A <anchor %d %d> mark @ACCENT;
} mark;
""" % (kav, kva, w_values[0], w_values[1], anchor_x, anchor_y)
    addOpenTypeFeaturesFromString(fb.font, feature)
    return fb.font


def build(workdir):
    doc = DesignSpaceDocument()
    axis = AxisDescriptor()
    axis.name, axis.tag = "Weight", "wght"
    axis.minimum, axis.default, axis.maximum = 100, 400, 900
    axis.labelNames = {"en": "Weight"}
    doc.addAxis(axis)
    for name, weight, kav, kva, w_values, ax, ay in MASTERS:
        font = master(name, kav, kva, w_values, ax, ay)
        path = os.path.join(workdir, "master-%s.ttf" % name)
        font.save(path)
        source = SourceDescriptor()
        source.path = path
        source.name = name
        source.location = {"Weight": weight}
        if name == "default":
            source.copyInfo = True
        doc.addSource(source)
    variable, _, _ = varlib_build(doc)
    return variable


def record_value(record, attribute):
    return getattr(record, attribute, 0) or 0


def read_gpos(font):
    """The numbers the tests check, from the GPOS of a (static) font."""
    gpos = font["GPOS"].table
    order = font.getGlyphOrder()
    out = {"kernAV": 0, "kernVA": 0, "wPlacement": 0, "wAdvance": 0, "anchorX": 0, "anchorY": 0}
    for lookup in gpos.LookupList.Lookup:
        for sub in lookup.SubTable:
            if hasattr(sub, "ExtSubTable"):
                sub = sub.ExtSubTable
            kind = type(sub).__name__
            if kind == "PairPos" and sub.Format == 1:
                cov = sub.Coverage.glyphs
                for first, pair_set in zip(cov, sub.PairSet):
                    for pvr in pair_set.PairValueRecord:
                        v = pvr.Value1
                        adv = record_value(v, "XAdvance")
                        if first == "A" and pvr.SecondGlyph == "V":
                            out["kernAV"] = adv
                        if first == "V" and pvr.SecondGlyph == "A":
                            out["kernVA"] = adv
            elif kind == "SinglePos":
                cov = sub.Coverage.glyphs
                values = [sub.Value] * len(cov) if sub.Format == 1 else sub.Value
                for glyph, v in zip(cov, values):
                    if glyph == "W":
                        out["wPlacement"] = record_value(v, "XPlacement")
                        out["wAdvance"] = record_value(v, "XAdvance")
            elif kind == "MarkBasePos":
                base = sub.BaseArray.BaseRecord[0].BaseAnchor[0]
                out["anchorX"] = base.XCoordinate
                out["anchorY"] = base.YCoordinate
    return out


def main():
    with tempfile.TemporaryDirectory() as workdir:
        font = build(workdir)
        font.save(OUT_FONT)

    golden = {}
    for weight in LOCATIONS:
        instance = instancer.instantiateVariableFont(TTFont(OUT_FONT), {"wght": weight}, inplace=False)
        golden[str(weight)] = read_gpos(instance)
    with open(OUT_GOLDEN, "w") as handle:
        json.dump(golden, handle, indent=1, sort_keys=True)
    print("wrote", OUT_FONT, os.path.getsize(OUT_FONT), "bytes and", OUT_GOLDEN)


if __name__ == "__main__":
    main()
