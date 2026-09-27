#!/usr/bin/env python3
"""Builds the variable colour-font test fixture VariableColorTest.ttf and the reference values the tests compare against.

The font is synthetic and released under CC0 (see VariableColorTest.LICENSE.txt): three layer glyphs (a box, a triangle and a second box)
and one colour glyph for each of the fourteen variable paint formats of COLR version 1 (PaintVarSolid, PaintVarLinearGradient,
PaintVarRadialGradient, PaintVarSweepGradient, PaintVarTransform, PaintVarTranslate, PaintVarScale, PaintVarScaleAroundCenter,
PaintVarScaleUniform, PaintVarScaleUniformAroundCenter, PaintVarRotate, PaintVarRotateAroundCenter, PaintVarSkew and
PaintVarSkewAroundCenter), with a variable colour line where the format has one, and a variable clip box for every colour glyph. The font
is assembled by fontTools' varLib from six masters on two axes (`wght` 100 to 900 and `wdth` 75 to 125, one master at an intermediate
weight so that the store gets an intermediate region), which merges the masters' COLR tables into one that has `VarIndexBase` fields, a
`DeltaSetIndexMap` and an ItemVariationStore.

The paints hold numbers that differ from master to master (`master_values` below). The reference file VariableColorTest.golden.json holds,
for a grid of locations, what those numbers are at the location: the numbers of each master interpolated by fontTools' own VariationModel,
which is what the store's deltas add up to (the store keeps whole-number deltas, so the engine agrees within one unit for the FWord fields
and within a few 1/16384 for the fixed-point ones). Each glyph's values are listed in the order the tests walk its paint graph.

fontTools' instancer does not instantiate COLR, so the reference is the masters themselves, not an instantiated font.

Run from anywhere: python assets/fonts/generate_variable_color_fixture.py
Requires fontTools (any recent version).
"""
import json
import math
import os
import tempfile

# Fixed timestamps, so that running the script again writes the same bytes.
os.environ.setdefault("SOURCE_DATE_EPOCH", "1767225600")

from fontTools.designspaceLib import AxisDescriptor, DesignSpaceDocument, SourceDescriptor
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont
from fontTools.ttLib.tables import otTables as ot
from fontTools.varLib import build as varlib_build
from fontTools.varLib.models import VariationModel, normalizeLocation

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_FONT = os.path.join(HERE, "VariableColorTest.ttf")
OUT_GOLDEN = os.path.join(HERE, "VariableColorTest.golden.json")

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

AXIS_LIMITS = {"wght": (100, 400, 900), "wdth": (75, 100, 125)}

# Palette: 0=red, 1=green, 2=blue, 3=yellow.
PALETTE = [(1.0, 0.0, 0.0, 1.0), (0.0, 0.5, 0.0, 1.0), (0.0, 0.0, 1.0, 1.0), (1.0, 1.0, 0.0, 1.0)]

# The colour glyphs, in glyph order after the layer glyphs: (glyph name, code point, paint format).
COLOR_GLYPHS = [
    ("solidV", 0x41, "PaintVarSolid"),
    ("linearV", 0x42, "PaintVarLinearGradient"),
    ("radialV", 0x43, "PaintVarRadialGradient"),
    ("sweepV", 0x44, "PaintVarSweepGradient"),
    ("transformV", 0x45, "PaintVarTransform"),
    ("translateV", 0x46, "PaintVarTranslate"),
    ("scaleV", 0x47, "PaintVarScale"),
    ("scaleCV", 0x48, "PaintVarScaleAroundCenter"),
    ("scaleUV", 0x49, "PaintVarScaleUniform"),
    ("scaleUCV", 0x4A, "PaintVarScaleUniformAroundCenter"),
    ("rotateV", 0x4B, "PaintVarRotate"),
    ("rotateCV", 0x4C, "PaintVarRotateAroundCenter"),
    ("skewV", 0x4D, "PaintVarSkew"),
    ("skewCV", 0x4E, "PaintVarSkewAroundCenter"),
]
KIND = {g: kind for g, _, kind in COLOR_GLYPHS}
GLYPH_ORDER = [".notdef", "space", "box", "tri", "circ"] + [g for g, _, _ in COLOR_GLYPHS]
CMAP = {0x20: "space"} | {cp: g for g, cp, _ in COLOR_GLYPHS}


def rect(pen, x0, y0, x1, y1):
    pen.moveTo((x0, y0)); pen.lineTo((x1, y0)); pen.lineTo((x1, y1)); pen.lineTo((x0, y1)); pen.closePath()


def outlines():
    glyphs = {}
    pen = TTGlyphPen(None); rect(pen, 100, 0, 900, 800); glyphs["box"] = pen.glyph()
    pen = TTGlyphPen(None)
    pen.moveTo((500, 800)); pen.lineTo((100, 100)); pen.lineTo((900, 100)); pen.closePath()
    glyphs["tri"] = pen.glyph()
    pen = TTGlyphPen(None); rect(pen, 200, 100, 800, 700); glyphs["circ"] = pen.glyph()
    for name in [".notdef", "space"] + [g for g, _, _ in COLOR_GLYPHS]:
        glyphs[name] = TTGlyphPen(None).glyph()
    return glyphs


def clamp(value, low, high):
    return max(low, min(high, value))


def stops(wf, wdf):
    """A variable colour line: the offset and the alpha of the middle stop and the alpha of the last one follow the axes."""
    a, b = wf - 1, wdf - 1
    return [
        (0.0, 0, 1.0),
        (round(clamp(0.5 + 0.12 * a + 0.2 * b, 0.05, 0.95), 4), 1, round(clamp(0.8 + 0.1 * a, 0.1, 1.0), 4)),
        (1.0, 2, round(clamp(0.9 + 0.3 * b, 0.1, 1.0), 4)),
    ]


def master_values(wf, wdf):
    """The numbers of every colour glyph in one master. The parameters are integers where the COLR field is an FWord and floats where it
    is a fixed-point value (angles are degrees here)."""
    a, b = wf - 1, wdf - 1
    return {
        "solidV": {"alpha": round(clamp(0.55 + 0.2 * a + 0.4 * b, 0.1, 1.0), 4)},
        "linearV": {"x0": int(100 * wdf), "y0": int(60 * a), "x1": int(900 * wdf), "y1": int(120 * a),
                    "x2": int(100 * wdf) + int(40 * a), "y2": 800 - int(50 * a), "stops": stops(wf, wdf)},
        "radialV": {"x0": int(500 + 240 * b), "y0": int(400 + 30 * a), "r0": int(20 + 15 * a),
                    "x1": int(500 - 160 * b), "y1": int(400 - 20 * a), "r1": int(380 + 60 * a), "stops": stops(wf, wdf)},
        "sweepV": {"centerX": int(500 + 320 * b), "centerY": int(400 + 40 * a), "startAngle": round(10 + 20 * a, 3),
                   "endAngle": round(10 + 20 * a + 385 + 15 * a + 20 * b, 3), "stops": stops(wf, wdf)},
        "transformV": {"xx": round(1 + 0.1 * a, 5), "yx": round(0.2 * b, 5), "xy": round(-0.05 * a, 5), "yy": round(1 - 0.08 * a, 5),
                       "dx": round(40 * a + 120 * b, 3), "dy": round(-30 * a, 3)},
        "translateV": {"dx": int(80 * a + 240 * b), "dy": int(-50 * a + 80 * b)},
        "scaleV": {"scaleX": round(0.7 + 0.15 * a, 5), "scaleY": round(0.6 + 0.8 * b + 0.1 * a, 5)},
        "scaleCV": {"scaleX": round(0.5 + 0.2 * a, 5), "scaleY": round(0.6 + 0.1 * a + 0.8 * b, 5),
                    "centerX": int(500 + 200 * b), "centerY": int(400 + 30 * a)},
        "scaleUV": {"scale": round(0.6 + 0.2 * a + 0.3 * b, 5)},
        "scaleUCV": {"scale": round(0.55 + 0.15 * a + 0.3 * b, 5), "centerX": int(500 + 160 * b), "centerY": int(400 + 20 * a)},
        "rotateV": {"angle": round(20 + 15 * a + 30 * b, 3)},
        "rotateCV": {"angle": round(25 + 10 * a + 40 * b, 3), "centerX": int(500 + 120 * b), "centerY": int(400 + 25 * a)},
        "skewV": {"xSkewAngle": round(10 + 8 * a, 3), "ySkewAngle": round(4 + 24 * b, 3)},
        "skewCV": {"xSkewAngle": round(12 + 5 * a, 3), "ySkewAngle": round(6 + 16 * b, 3),
                   "centerX": int(500 + 80 * b), "centerY": int(400 + 15 * a)},
    }


def clip_box(wf, wdf):
    a, b = wf - 1, wdf - 1
    return (int(50 - 80 * b), int(-40 - 15 * a), int(950 + 120 * b + 20 * a), int(850 + 25 * a))


def paint_dict(glyph, values):
    """The paint graph of one colour glyph in one master, as a dict for fontTools' colorLib builder, holding `values`. The masters use the\n    plain paint formats; varLib's COLR merger turns the ones whose numbers differ into the variable formats."""
    F = ot.PaintFormat
    v = values[glyph]
    box = {"Format": F.PaintGlyph, "Glyph": "box", "Paint": {"Format": F.PaintSolid, "PaletteIndex": 0, "Alpha": 1.0}}
    tri = {"Format": F.PaintGlyph, "Glyph": "tri", "Paint": {"Format": F.PaintSolid, "PaletteIndex": 1, "Alpha": 1.0}}

    def line(stop_values):
        return {"ColorStop": [{"StopOffset": o, "PaletteIndex": p, "Alpha": al} for o, p, al in stop_values], "Extend": ot.ExtendMode.PAD}

    def clipped(paint):
        return {"Format": F.PaintGlyph, "Glyph": "box", "Paint": paint}

    if glyph == "solidV":
        return clipped({"Format": F.PaintSolid, "PaletteIndex": 0, "Alpha": v["alpha"]})
    if glyph == "linearV":
        return clipped({"Format": F.PaintLinearGradient, "ColorLine": line(v["stops"]),
                        "x0": v["x0"], "y0": v["y0"], "x1": v["x1"], "y1": v["y1"], "x2": v["x2"], "y2": v["y2"]})
    if glyph == "radialV":
        return clipped({"Format": F.PaintRadialGradient, "ColorLine": line(v["stops"]),
                        "x0": v["x0"], "y0": v["y0"], "r0": v["r0"], "x1": v["x1"], "y1": v["y1"], "r1": v["r1"]})
    if glyph == "sweepV":
        return clipped({"Format": F.PaintSweepGradient, "ColorLine": line(v["stops"]),
                        "centerX": v["centerX"], "centerY": v["centerY"], "startAngle": v["startAngle"], "endAngle": v["endAngle"]})

    wrappers = {
        "transformV": {"Format": F.PaintTransform, "Transform": (v.get("xx"), v.get("yx"), v.get("xy"), v.get("yy"), v.get("dx"), v.get("dy"))},
        "translateV": {"Format": F.PaintTranslate, "dx": v.get("dx"), "dy": v.get("dy")},
        "scaleV": {"Format": F.PaintScale, "scaleX": v.get("scaleX"), "scaleY": v.get("scaleY")},
        "scaleCV": {"Format": F.PaintScaleAroundCenter, "scaleX": v.get("scaleX"), "scaleY": v.get("scaleY"),
                    "centerX": v.get("centerX"), "centerY": v.get("centerY")},
        "scaleUV": {"Format": F.PaintScaleUniform, "scale": v.get("scale")},
        "scaleUCV": {"Format": F.PaintScaleUniformAroundCenter, "scale": v.get("scale"), "centerX": v.get("centerX"), "centerY": v.get("centerY")},
        "rotateV": {"Format": F.PaintRotate, "angle": v.get("angle")},
        "rotateCV": {"Format": F.PaintRotateAroundCenter, "angle": v.get("angle"), "centerX": v.get("centerX"), "centerY": v.get("centerY")},
        "skewV": {"Format": F.PaintSkew, "xSkewAngle": v.get("xSkewAngle"), "ySkewAngle": v.get("ySkewAngle")},
        "skewCV": {"Format": F.PaintSkewAroundCenter, "xSkewAngle": v.get("xSkewAngle"), "ySkewAngle": v.get("ySkewAngle"),
                   "centerX": v.get("centerX"), "centerY": v.get("centerY")},
    }
    wrapper = wrappers[glyph]
    wrapper["Paint"] = tri if glyph in ("rotateV", "skewV", "translateV", "scaleUV") else box
    return wrapper


def build_master(name, wf, wdf):
    values = master_values(wf, wdf)
    fb = FontBuilder(UPM, isTTF=True)
    fb.setupGlyphOrder(GLYPH_ORDER)
    fb.setupCharacterMap(CMAP)
    fb.setupGlyf(outlines())
    fb.setupHorizontalMetrics({g: (1000 if g != "space" else 500, 0) for g in GLYPH_ORDER})
    fb.setupHorizontalHeader(ascent=850, descent=-200)
    fb.setupNameTable({"familyName": "Variable Color Test", "styleName": "Regular"})
    fb.setupOS2(sTypoAscender=850, sTypoDescender=-200, usWinAscent=900, usWinDescent=220, version=4, fsSelection=0x40)
    fb.setupPost()
    fb.setupCPAL([PALETTE])
    colr = {g: paint_dict(g, values) for g, _, _ in COLOR_GLYPHS}
    clips = {g: clip_box(wf, wdf) for g, _, _ in COLOR_GLYPHS}
    fb.setupCOLR(colr, version=1, clipBoxes=clips)
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
        path = os.path.join(workdir, "color-master-%s.ttf" % name)
        build_master(name, wf, wdf).save(path)
        source = SourceDescriptor()
        source.path = path
        source.name = name
        source.location = {"Weight": dw, "Width": dwd}
        if name == "default":
            source.copyInfo = True
        doc.addSource(source)
    variable, _, _ = varlib_build(doc)
    return variable


# ---- the reference values ------------------------------------------------------------------------------------------------------------

def affine(kind, p):
    """The matrix of a transform paint from its parameters, as (xx, yx, xy, yy, dx, dy): x' = xx*x + xy*y + dx and y' = yx*x + yy*y + dy."""
    def around(m, cx, cy):
        # translate(c) * m * translate(-c)
        xx, yx, xy, yy, dx, dy = m
        return (xx, yx, xy, yy, dx + cx - xx * cx - xy * cy, dy + cy - yx * cx - yy * cy)

    if kind == "PaintVarTransform":
        return (p["xx"], p["yx"], p["xy"], p["yy"], p["dx"], p["dy"])
    if kind == "PaintVarTranslate":
        return (1, 0, 0, 1, p["dx"], p["dy"])
    if kind == "PaintVarScale":
        return (p["scaleX"], 0, 0, p["scaleY"], 0, 0)
    if kind == "PaintVarScaleAroundCenter":
        return around((p["scaleX"], 0, 0, p["scaleY"], 0, 0), p["centerX"], p["centerY"])
    if kind == "PaintVarScaleUniform":
        return (p["scale"], 0, 0, p["scale"], 0, 0)
    if kind == "PaintVarScaleUniformAroundCenter":
        return around((p["scale"], 0, 0, p["scale"], 0, 0), p["centerX"], p["centerY"])
    if kind in ("PaintVarRotate", "PaintVarRotateAroundCenter"):
        r = math.radians(p["angle"])
        m = (math.cos(r), math.sin(r), -math.sin(r), math.cos(r), 0, 0)
        return around(m, p["centerX"], p["centerY"]) if kind.endswith("AroundCenter") else m
    if kind in ("PaintVarSkew", "PaintVarSkewAroundCenter"):
        m = (1, math.tan(math.radians(p["ySkewAngle"])), -math.tan(math.radians(p["xSkewAngle"])), 1, 0, 0)
        return around(m, p["centerX"], p["centerY"]) if kind.endswith("AroundCenter") else m
    raise ValueError(kind)


def flatten(kind, p):
    """The numbers of one glyph's paint graph in the order the tests walk it: the paint's own numbers (angles in radians, a transform as
    its matrix), then the colour line's stops as offset and alpha."""
    if kind == "PaintVarSolid":
        out = [p["alpha"]]
    elif kind == "PaintVarLinearGradient":
        out = [p[k] for k in ("x0", "y0", "x1", "y1", "x2", "y2")]
    elif kind == "PaintVarRadialGradient":
        out = [p[k] for k in ("x0", "y0", "r0", "x1", "y1", "r1")]
    elif kind == "PaintVarSweepGradient":
        out = [p["centerX"], p["centerY"], math.radians(p["startAngle"]), math.radians(p["endAngle"])]
    else:
        # A transform paints a green triangle or box through a plain PaintSolid of alpha 1 (walked as one more number).
        out = list(affine(kind, p)) + [1.0]
    for offset, _, alpha in p.get("stops", []):
        out += [offset, alpha]
    return out


def interpolate(model, per_master, location):
    """Interpolates a nested structure of numbers (dicts, lists, tuples) from the masters at a normalized location."""
    first = per_master[0]
    if isinstance(first, dict):
        return {k: interpolate(model, [m[k] for m in per_master], location) for k in first}
    if isinstance(first, (list, tuple)):
        return [interpolate(model, [m[i] for m in per_master], location) for i in range(len(first))]
    return model.interpolateFromMasters(location, list(per_master))


def build_golden():
    master_locations = [normalizeLocation({"wght": dw, "wdth": dwd}, AXIS_LIMITS) for _, dw, dwd, _, _ in MASTERS]
    model = VariationModel(master_locations, ["wght", "wdth"])
    per_master_values = [master_values(wf, wdf) for _, _, _, wf, wdf in MASTERS]
    per_master_clips = [clip_box(wf, wdf) for _, _, _, wf, wdf in MASTERS]

    golden = {"glyphs": KIND, "locations": []}
    for loc in LOCATIONS:
        normalized = normalizeLocation(loc, AXIS_LIMITS)
        entry = {"location": loc, "values": {}, "clips": {}}
        for glyph, _, kind in COLOR_GLYPHS:
            # The engine adds deltas to the parameters and then builds the matrix, so the parameters are interpolated first.
            params = interpolate(model, [values[glyph] for values in per_master_values], normalized)
            entry["values"][glyph] = flatten(kind, params)
            entry["clips"][glyph] = [model.interpolateFromMasters(normalized, [c[i] for c in per_master_clips]) for i in range(4)]
        golden["locations"].append(entry)
    return golden


def main():
    with tempfile.TemporaryDirectory() as workdir:
        build_variable_font(workdir).save(OUT_FONT)

    font = TTFont(OUT_FONT)
    colr = font["COLR"].table
    assert {"COLR", "CPAL", "fvar"} <= set(font.keys()), sorted(font.keys())
    assert colr.VarStore is not None, "the merged COLR has no variation store"

    with open(OUT_GOLDEN, "w", encoding="utf-8", newline="\n") as f:
        json.dump(build_golden(), f, indent=1)
        f.write("\n")
    print("tables:", " ".join(sorted(font.keys())))
    print("wrote", OUT_FONT, os.path.getsize(OUT_FONT), "bytes and", OUT_GOLDEN)


if __name__ == "__main__":
    main()
