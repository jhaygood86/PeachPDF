#!/usr/bin/env python3
"""Builds the variable-font test fixture VariableTest.ttf and the reference values the tests compare against.

The font is synthetic and released under CC0 (see VariableTest.LICENSE.txt): a handful of geometric glyphs on two axes,
`wght` (100 to 900, default 400, with an `avar` segment map) and `wdth` (75 to 125, default 100). It is assembled with
fontTools' varLib from six masters, one of which sits at an intermediate weight so that `gvar` gets an intermediate region,
and it carries the tables the engine reads: `fvar`, `avar`, `gvar`, `HVAR` and `MVAR`. VariableTestNoHvar.ttf is the same font with `HVAR` removed.

Glyphs (each chosen to exercise something):

* A       - a simple glyph of straight lines with a counter contour: every point moves, so all points have deltas
* B       - quadratic curves, including two consecutive off-curve points (an implied on-curve point between them)
* C       - twelve points along a curve where the interior points interpolate linearly, so varLib keeps deltas for only some
            points and the reader has to interpolate the rest (IUP)
* acute   - a small shape
* Aacute  - a composite of A and acute whose component offset changes with the axes (gvar deltas on the offsets)
* space   - an empty glyph

The reference file VariableTest.golden.json holds, for a grid of axis locations, what fontTools' own instancer
(fontTools.varLib.instancer.instantiateVariableFont) produces: every glyph's outline as cubic segments (quadratics raised the way
the engine raises them), its advance width, and the font-wide metrics MVAR varies. The tests compare the engine with it, within one
design unit (the instancer rounds coordinates to integers, the engine does not).

Run from anywhere: python assets/fonts/generate_variable_fixture.py
Requires fontTools (any recent version).
"""
import json
import os
import tempfile

# Fixed timestamps, so that running the script again writes the same bytes.
os.environ.setdefault("SOURCE_DATE_EPOCH", "1767225600")

from fontTools.designspaceLib import AxisDescriptor, DesignSpaceDocument, InstanceDescriptor, SourceDescriptor
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont
from fontTools.varLib import build as varlib_build
from fontTools.varLib import instancer

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_FONT = os.path.join(HERE, "VariableTest.ttf")
OUT_FONT_NO_HVAR = os.path.join(HERE, "VariableTestNoHvar.ttf")
OUT_GOLDEN = os.path.join(HERE, "VariableTest.golden.json")

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

GLYPH_ORDER = [".notdef", "space", "A", "B", "C", "acute", "Aacute"]
CMAP = {0x20: "space", 0x41: "A", 0x42: "B", 0x43: "C", 0xB4: "acute", 0xC1: "Aacute"}


def stem(weight):
    return int(round(40 + 45 * weight))


def build_master(name, weight, width):
    t = stem(weight)
    sx = width
    glyphs = {}

    def pen():
        return TTGlyphPen(glyphs)

    # .notdef: a box with a counter
    p = pen()
    p.moveTo((50, 0)); p.lineTo((50, 700)); p.lineTo((450, 700)); p.lineTo((450, 0)); p.closePath()
    p.moveTo((50 + t, t)); p.lineTo((450 - t, t)); p.lineTo((450 - t, 700 - t)); p.lineTo((50 + t, 700 - t)); p.closePath()
    glyphs[".notdef"] = p.glyph()

    glyphs["space"] = pen().glyph()

    # A: trapezoid with a triangular counter
    p = pen()
    p.moveTo((int(50 * sx), 0))
    p.lineTo((int(250 * sx) - t // 2, 700))
    p.lineTo((int(250 * sx) + t // 2, 700))
    p.lineTo((int(450 * sx), 0))
    p.lineTo((int(450 * sx) - t, 0))
    p.lineTo((int(50 * sx) + t, 0))
    p.closePath()
    p.moveTo((int(250 * sx), 100 + t))
    p.lineTo((int(250 * sx) - t, 100 + t))
    p.lineTo((int(250 * sx), 100 + 3 * t))
    p.closePath()
    glyphs["A"] = p.glyph()

    # B: a stem with a rounded bowl; two consecutive off-curve points give an implied on-curve point
    p = pen()
    p.moveTo((int(60 * sx), 0))
    p.lineTo((int(60 * sx), 700))
    p.lineTo((int(240 * sx), 700))
    p.qCurveTo((int(420 * sx) + t, 700), (int(420 * sx) + t, 450), (int(240 * sx), 350 + t // 2))
    p.qCurveTo((int(460 * sx) + t, 300), (int(460 * sx) + t, 150), (int(240 * sx), 0))
    p.closePath()
    glyphs["B"] = p.glyph()

    # C: twelve points along a curve whose interior points move linearly between masters
    p = pen()
    outer = []
    inner = []
    for i in range(6):
        y = 100 + i * 100
        outer.append((int((80 + 40 * i) * sx), y))
        inner.append((int((80 + 40 * i) * sx) + t, y))
    p.moveTo(outer[0])
    for pt in outer[1:]:
        p.lineTo(pt)
    for pt in reversed(inner):
        p.lineTo(pt)
    p.closePath()
    glyphs["C"] = p.glyph()

    # acute
    p = pen()
    p.moveTo((0, 0)); p.lineTo((int(60 * sx) + t // 2, 120)); p.lineTo((int(60 * sx) + t // 2 + 40, 120)); p.lineTo((40, 0)); p.closePath()
    glyphs["acute"] = p.glyph()

    # Aacute: A plus acute, the accent's offset following the axes
    p = pen()
    p.addComponent("A", (1, 0, 0, 1, 0, 0))
    p.addComponent("acute", (1, 0, 0, 1, int(120 * sx) + t // 3, 720 + t // 2))
    glyphs["Aacute"] = p.glyph()

    fb = FontBuilder(UPM, isTTF=True)
    fb.setupGlyphOrder(GLYPH_ORDER)
    fb.setupCharacterMap(CMAP)
    fb.setupGlyf(glyphs)

    metrics = {}
    advance = {".notdef": int(500 * sx), "space": int(250 * sx), "A": int(500 * sx) + t // 2, "B": int(520 * sx) + t,
               "C": int(400 * sx) + t, "acute": int(160 * sx) + t // 2, "Aacute": int(500 * sx) + t // 2}
    for gn in GLYPH_ORDER:
        g = fb.font["glyf"][gn]
        g.recalcBounds(fb.font["glyf"])
        metrics[gn] = (advance[gn], getattr(g, "xMin", 0))
    fb.setupHorizontalMetrics(metrics)

    ascent = 800 + int(round(20 * weight))
    descent = -(200 + int(round(10 * weight)))
    fb.setupHorizontalHeader(ascent=ascent, descent=descent, lineGap=int(round(10 * weight)))
    fb.setupNameTable({"familyName": "Variable Test", "styleName": "Regular"})
    fb.setupOS2(
        sTypoAscender=ascent, sTypoDescender=descent, sTypoLineGap=int(round(10 * weight)),
        usWinAscent=ascent + 50, usWinDescent=-descent + 20,
        sxHeight=480 + int(round(15 * weight)), sCapHeight=700 + int(round(10 * weight)),
        ySubscriptYOffset=140 + int(round(5 * weight)), ySuperscriptYOffset=480 + int(round(5 * weight)),
        yStrikeoutPosition=260 + int(round(6 * weight)), yStrikeoutSize=50 + int(round(4 * weight)),
        version=4, fsSelection=0x40,
    )
    fb.setupPost(underlinePosition=-100 - int(round(6 * weight)), underlineThickness=40 + int(round(5 * weight)))
    return fb.font


def build_variable_font(workdir):
    doc = DesignSpaceDocument()

    weight = AxisDescriptor()
    weight.name, weight.tag = "Weight", "wght"
    weight.minimum, weight.default, weight.maximum = 100, 400, 900
    weight.map = [(100, 100), (400, 400), (700, 600), (900, 900)]
    weight.labelNames = {"en": "Weight"}
    doc.addAxis(weight)

    width = AxisDescriptor()
    width.name, width.tag = "Width", "wdth"
    width.minimum, width.default, width.maximum = 75, 100, 125
    width.labelNames = {"en": "Width"}
    doc.addAxis(width)

    for name, design_weight, design_width, wf, wdf in MASTERS:
        font = build_master(name, wf, wdf)
        path = os.path.join(workdir, "master-%s.ttf" % name)
        font.save(path)
        source = SourceDescriptor()
        source.path = path
        source.name = name
        source.location = {"Weight": design_weight, "Width": design_width}
        if name == "default":
            source.copyInfo = True
        doc.addSource(source)

    # Named instances, which fvar records with their names (locations are in design coordinates: 600 is user weight 700).
    for style, wght, wdth in (("Light", 250, 100), ("Bold", 600, 100), ("Bold Condensed", 600, 75)):
        instance = InstanceDescriptor()
        instance.familyName = "Variable Test"
        instance.styleName = style
        instance.location = {"Weight": wght, "Width": wdth}
        doc.addInstance(instance)

    variable, _, _ = varlib_build(doc)
    return variable


LOCATIONS = [
    {"wght": 400, "wdth": 100},   # the default
    {"wght": 100, "wdth": 100},
    {"wght": 250, "wdth": 100},
    {"wght": 700, "wdth": 100},   # the intermediate master through the avar map
    {"wght": 900, "wdth": 100},
    {"wght": 550, "wdth": 100},
    {"wght": 400, "wdth": 75},
    {"wght": 400, "wdth": 110},
    {"wght": 900, "wdth": 75},
    {"wght": 600, "wdth": 90},
    {"wght": 1000, "wdth": 100},  # beyond the axis: clamps to 900
    {"wght": 50, "wdth": 200},    # clamps on both axes
]


def contour_segments(points, ends, flags):
    """The outline of one glyph as the engine builds it: closed contours of ('M'|'L'|'C', ...) with quadratics raised to cubics."""
    contours = []
    start = 0
    for end in ends:
        pts = [(points[i][0], points[i][1], bool(flags[i] & 1)) for i in range(start, end + 1)]
        start = end + 1
        n = len(pts)
        if n == 0:
            continue
        first_on = next((i for i, p in enumerate(pts) if p[2]), -1)
        if first_on < 0:
            sx = ((pts[0][0] + pts[-1][0]) / 2.0, (pts[0][1] + pts[-1][1]) / 2.0)
            seq = pts + [(sx[0], sx[1], True)]
        else:
            sx = (pts[first_on][0], pts[first_on][1])
            seq = [pts[(first_on + i) % n] for i in range(1, n + 1)]
        segs = [["M", sx[0], sx[1]]]
        cur = sx
        pending = None
        for (x, y, on) in seq:
            if on:
                if pending is not None:
                    segs.append(cubic(cur, pending, (x, y)))
                    pending = None
                else:
                    segs.append(["L", x, y])
                cur = (x, y)
            elif pending is not None:
                mid = ((pending[0] + x) / 2.0, (pending[1] + y) / 2.0)
                segs.append(cubic(cur, pending, mid))
                cur = mid
                pending = (x, y)
            else:
                pending = (x, y)
        contours.append(segs)
    return contours


def cubic(start, control, end):
    c1 = (start[0] + 2.0 / 3.0 * (control[0] - start[0]), start[1] + 2.0 / 3.0 * (control[1] - start[1]))
    c2 = (end[0] + 2.0 / 3.0 * (control[0] - end[0]), end[1] + 2.0 / 3.0 * (control[1] - end[1]))
    return ["C", c1[0], c1[1], c2[0], c2[1], end[0], end[1]]


def describe(font):
    glyf = font["glyf"]
    hmtx = font["hmtx"]
    glyphs = {}
    for gn in font.getGlyphOrder():
        g = glyf[gn]
        if g.numberOfContours == 0:
            outline = []
        else:
            coords, ends, flags = g.getCoordinates(glyf)
            outline = contour_segments(list(coords), list(ends), list(flags))
        glyphs[gn] = {"advance": hmtx[gn][0], "outline": outline}
    os2 = font["OS/2"]
    hhea = font["hhea"]
    post = font["post"]
    metrics = {
        "hheaAscender": hhea.ascent, "hheaDescender": hhea.descent, "hheaLineGap": hhea.lineGap,
        "typoAscender": os2.sTypoAscender, "typoDescender": os2.sTypoDescender, "typoLineGap": os2.sTypoLineGap,
        "winAscent": os2.usWinAscent, "winDescent": os2.usWinDescent,
        "xHeight": os2.sxHeight, "capHeight": os2.sCapHeight,
        "strikeoutPosition": os2.yStrikeoutPosition, "strikeoutSize": os2.yStrikeoutSize,
        "underlinePosition": post.underlinePosition, "underlineThickness": post.underlineThickness,
    }
    return {"glyphs": glyphs, "metrics": metrics}


def main():
    with tempfile.TemporaryDirectory() as workdir:
        variable = build_variable_font(workdir)
        variable.save(OUT_FONT)

    # The same font without HVAR, so that an advance has to come from the phantom points of gvar.
    without_hvar = TTFont(OUT_FONT)
    del without_hvar["HVAR"]
    without_hvar.save(OUT_FONT_NO_HVAR)

    reference = TTFont(OUT_FONT)
    tags = sorted(reference.keys())
    assert {"fvar", "avar", "gvar", "HVAR", "MVAR"} <= set(tags), tags

    golden = {"locations": []}
    for loc in LOCATIONS:
        instance = instancer.instantiateVariableFont(TTFont(OUT_FONT), dict(loc), inplace=False)
        entry = describe(instance)
        entry["location"] = loc
        golden["locations"].append(entry)

    with open(OUT_GOLDEN, "w", encoding="utf-8", newline="\n") as f:
        json.dump(golden, f, indent=1)
        f.write("\n")
    print("tables:", " ".join(tags))
    print("wrote", OUT_FONT, os.path.getsize(OUT_FONT), "bytes and", OUT_GOLDEN)


if __name__ == "__main__":
    main()
