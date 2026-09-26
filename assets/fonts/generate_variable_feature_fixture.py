#!/usr/bin/env python3
"""Builds VariableFeatureTest.ttf (CC0, see VariableFeatureTest.LICENSE.txt): a variable font whose GSUB has FeatureVariations.

One `wght` axis (100 to 900, default 400). Glyphs: A, B, and A.heavy, B.heavy. The design space has two rules, which fontTools' varLib
turns into `rvrn` feature variations:

* wght from 600 to 900: A is replaced by A.heavy
* wght from 800 to 900: B is replaced by B.heavy as well

varLib splits these into two records, in this order: the narrower region (800 to 900, both swaps) first, then 600 to 800 (A only). The first
record whose conditions hold wins, and a location outside both regions keeps the font's own features. The glyphs are rectangles of
different widths, so a substitution shows in the advance: A is 500, A.heavy 700, B 500, B.heavy 900.
Run: python assets/fonts/generate_variable_feature_fixture.py. Requires fontTools.
"""
import os
import tempfile

os.environ.setdefault("SOURCE_DATE_EPOCH", "1767225600")

from fontTools.designspaceLib import AxisDescriptor, DesignSpaceDocument, RuleDescriptor, SourceDescriptor
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.varLib import build as varlib_build

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "VariableFeatureTest.ttf")

ORDER = [".notdef", "A", "B", "A.heavy", "B.heavy"]
ADVANCE = {".notdef": 500, "A": 500, "B": 500, "A.heavy": 700, "B.heavy": 900}


def master():
    glyphs = {}
    for name in ORDER:
        pen = TTGlyphPen(glyphs)
        w = ADVANCE[name]
        pen.moveTo((50, 0)); pen.lineTo((50, 700)); pen.lineTo((w - 50, 700)); pen.lineTo((w - 50, 0)); pen.closePath()
        glyphs[name] = pen.glyph()
    fb = FontBuilder(1000, isTTF=True)
    fb.setupGlyphOrder(ORDER)
    fb.setupCharacterMap({0x41: "A", 0x42: "B"})
    fb.setupGlyf(glyphs)
    fb.setupHorizontalMetrics({n: (ADVANCE[n], 50) for n in ORDER})
    fb.setupHorizontalHeader(ascent=800, descent=-200)
    fb.setupNameTable({"familyName": "Variable Feature Test", "styleName": "Regular"})
    fb.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200, version=4)
    fb.setupPost()
    return fb.font


def main():
    with tempfile.TemporaryDirectory() as work:
        doc = DesignSpaceDocument()
        axis = AxisDescriptor()
        axis.name, axis.tag = "Weight", "wght"
        axis.minimum, axis.default, axis.maximum = 100, 400, 900
        axis.labelNames = {"en": "Weight"}
        doc.addAxis(axis)
        for name, weight in (("default", 400), ("light", 100), ("black", 900)):
            path = os.path.join(work, name + ".ttf")
            master().save(path)
            source = SourceDescriptor()
            source.path = path
            source.name = name
            source.location = {"Weight": weight}
            if name == "default":
                source.copyInfo = True
            doc.addSource(source)

        first = RuleDescriptor()
        first.name = "heavy-a"
        first.conditionSets = [[{"name": "Weight", "minimum": 600, "maximum": 900}]]
        first.subs = [("A", "A.heavy")]
        doc.addRule(first)

        second = RuleDescriptor()
        second.name = "heavy-b"
        second.conditionSets = [[{"name": "Weight", "minimum": 800, "maximum": 900}]]
        second.subs = [("B", "B.heavy")]
        doc.addRule(second)

        variable, _, _ = varlib_build(doc)
        variable.save(OUT)
    print("wrote", OUT, os.path.getsize(OUT), "bytes")


if __name__ == "__main__":
    main()
