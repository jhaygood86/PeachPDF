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

A second font, VariableCff2Test.otf, has CFF2 outlines (variable CFF) on the same two axes, with its own reference file
VariableCff2Test.golden.json made the same way (the instancer draws the CFF2 charstrings it instantiates). Its glyphs, each
chosen to exercise something:

* A       - straight lines and a counter whose coordinates blend (`blend` inside `hlineto`/`vlineto` runs)
* B       - cubic curves whose control points blend
* C       - everything after the moveto is a local subroutine (`callsubr`)
* D       - starts with `vsindex 1`, a second ItemVariationData that names two of the five regions, and blends six operands at once
* E       - a second Font DICT (FDSelect picks it) whose Private DICT sets `vsindex 2`, with a local subroutine of its own
* F       - the path is a global subroutine (`callgsubr`), blended over the five regions of ItemVariationData 0
* space   - an empty charstring

varLib writes A and B; D, E, F and the subroutines of C are written by hand afterwards (`write_cff2_charstrings`), because varLib
emits neither subroutines nor a `vsindex` other than the default.

Run from anywhere: python assets/fonts/generate_variable_fixture.py
Requires fontTools (any recent version).
"""
import copy
import io
import json
import os
import tempfile

# Fixed timestamps, so that running the script again writes the same bytes.
os.environ.setdefault("SOURCE_DATE_EPOCH", "1767225600")

from fontTools.cffLib import FDSelect, SubrsIndex
from fontTools.designspaceLib import AxisDescriptor, DesignSpaceDocument, InstanceDescriptor, SourceDescriptor
from fontTools.fontBuilder import FontBuilder
from fontTools.misc.psCharStrings import T2CharString
from fontTools.misc.roundTools import otRound
from fontTools.pens.recordingPen import RecordingPen
from fontTools.pens.t2CharStringPen import T2CharStringPen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTFont
from fontTools.ttLib.tables import otTables
from fontTools.varLib import build as varlib_build
from fontTools.varLib import builder, instancer
from fontTools.varLib.models import normalizeLocation
from fontTools.varLib.varStore import VarStoreInstancer

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_FONT = os.path.join(HERE, "VariableTest.ttf")
OUT_FONT_NO_HVAR = os.path.join(HERE, "VariableTestNoHvar.ttf")
OUT_GOLDEN = os.path.join(HERE, "VariableTest.golden.json")
OUT_CFF2_FONT = os.path.join(HERE, "VariableCff2Test.otf")
OUT_CFF2_GOLDEN = os.path.join(HERE, "VariableCff2Test.golden.json")
OUT_VERTICAL_FONT = os.path.join(HERE, "VariableVerticalTest.ttf")
OUT_VERTICAL_FONT_NO_VVAR = os.path.join(HERE, "VariableVerticalTestNoVvar.ttf")
OUT_VERTICAL_GOLDEN = os.path.join(HERE, "VariableVerticalTest.golden.json")
OUT_CFF2_VERTICAL_FONT = os.path.join(HERE, "VariableCff2VerticalTest.otf")
OUT_CFF2_VERTICAL_GOLDEN = os.path.join(HERE, "VariableCff2VerticalTest.golden.json")
OUT_AVAR2_FONT = os.path.join(HERE, "VariableAvar2Test.ttf")
OUT_AVAR2_GOLDEN = os.path.join(HERE, "VariableAvar2Test.golden.json")

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


def add_vertical_metrics(fb, weight, sx):
    """`vhea` and `vmtx` for a master: an advance height and a top side bearing per glyph that both follow the weight and the width, so
    that the vertical advance varies (through the phantom points of gvar, and through VVAR that varLib writes from the same metrics)."""
    metrics = {}
    for gn in fb.font.getGlyphOrder():
        glyph = fb.font["glyf"][gn]
        advance = 900 + int(round(60 * weight)) + int(round(80 * (sx - 1))) + (30 if gn == "A" else 0)
        metrics[gn] = (advance, 100 + int(round(12 * weight)) if getattr(glyph, "numberOfContours", 0) else 0)
    fb.setupVerticalMetrics(metrics)
    fb.setupVerticalHeader(ascent=840 + int(round(18 * weight)), descent=-(160 + int(round(8 * weight))), lineGap=int(round(4 * weight)))


def build_master(name, weight, width, vertical=False):
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

    if vertical:
        add_vertical_metrics(fb, weight, sx)

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


def build_variable_font(workdir, vertical=False):
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
        font = build_master(name, wf, wdf, vertical)
        path = os.path.join(workdir, "master-%s%s.ttf" % (name, "-v" if vertical else ""))
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


CFF2_GLYPH_ORDER = [".notdef", "space", "A", "B", "C", "D", "E", "F"]
CFF2_CMAP = {0x20: "space", 0x41: "A", 0x42: "B", 0x43: "C", 0x44: "D", 0x45: "E", 0x46: "F"}


def add_cff_vertical_metrics(fb, weight, sx):
    """`vhea`, `vmtx` and `VORG` for a master of the CFF2 font: the advance height and the vertical origin of a glyph follow the weight
    and the width (varLib writes a VVAR with an advance mapping and a vertical origin mapping from them)."""
    advances = {}
    origins = {}
    for gn in CFF2_GLYPH_ORDER:
        advances[gn] = (900 + int(round(60 * weight)) + int(round(80 * (sx - 1))) + (30 if gn == "A" else 0), 0)
        origins[gn] = 850 + int(round(25 * weight)) + int(round(40 * (sx - 1))) + (35 if gn == "B" else 0)
    fb.setupVerticalMetrics(advances)
    fb.setupVerticalHeader(ascent=840 + int(round(18 * weight)), descent=-(160 + int(round(8 * weight))), lineGap=int(round(4 * weight)))
    fb.setupVerticalOrigins(origins, defaultVerticalOrigin=850 + int(round(25 * weight)) + int(round(40 * (sx - 1))))


def build_cff_master(name, weight, width, vertical=False):
    """One master of the CFF2 font, as a CFF (version 1) font that varLib merges into CFF2. D, E and F are stand-ins here: their
    charstrings are written by hand in write_cff2_charstrings once the masters are merged."""
    t = stem(weight)
    sx = width
    charstrings = {}
    advances = {}

    def pen(glyph, advance):
        advances[glyph] = advance
        return T2CharStringPen(advance, None)

    p = pen(".notdef", 500)
    p.moveTo((50, 0)); p.lineTo((50, 700)); p.lineTo((450, 700)); p.lineTo((450, 0)); p.closePath()
    p.moveTo((50 + t, t)); p.lineTo((450 - t, t)); p.lineTo((450 - t, 700 - t)); p.lineTo((50 + t, 700 - t)); p.closePath()
    charstrings[".notdef"] = p.getCharString()

    charstrings["space"] = pen("space", int(250 * sx)).getCharString()

    p = pen("A", int(500 * sx) + t // 2)
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
    charstrings["A"] = p.getCharString()

    p = pen("B", int(520 * sx) + t)
    p.moveTo((int(60 * sx), 0))
    p.lineTo((int(60 * sx), 700))
    p.curveTo((int(300 * sx) + t, 700), (int(420 * sx) + t, 500), (int(420 * sx) + t, 300))
    p.curveTo((int(420 * sx) + t, 100), (int(300 * sx), 0), (int(60 * sx), 0))
    p.closePath()
    charstrings["B"] = p.getCharString()

    p = pen("C", int(460 * sx) + t)
    p.moveTo((int(60 * sx), 0))
    p.lineTo((int(60 * sx), 700))
    p.lineTo((int(60 * sx) + t, 700))
    p.lineTo((int(60 * sx) + t, t))
    p.lineTo((int(400 * sx), t))
    p.lineTo((int(400 * sx), 0))
    p.closePath()
    charstrings["C"] = p.getCharString()

    for glyph, advance in (("D", 460), ("E", 480), ("F", 440)):
        p = pen(glyph, int(advance * sx) + t // 2)
        p.moveTo((int(60 * sx), 0)); p.lineTo((int(60 * sx), 600)); p.lineTo((int(360 * sx) + t, 0)); p.closePath()
        charstrings[glyph] = p.getCharString()

    fb = FontBuilder(UPM, isTTF=False)
    fb.setupGlyphOrder(CFF2_GLYPH_ORDER)
    fb.setupCharacterMap(CFF2_CMAP)
    fb.setupCFF("VariableCff2Test-" + name, {"FullName": "Variable Cff2 Test " + name}, charstrings, {})
    fb.setupHorizontalMetrics({glyph: (advances[glyph], 0) for glyph in CFF2_GLYPH_ORDER})
    if vertical:
        add_cff_vertical_metrics(fb, weight, sx)
    ascent = 800 + int(round(20 * weight))
    fb.setupHorizontalHeader(ascent=ascent, descent=-200)
    fb.setupNameTable({"familyName": "Variable Cff2 Test", "styleName": "Regular"})
    fb.setupOS2(sTypoAscender=ascent, sTypoDescender=-200, usWinAscent=ascent + 50, usWinDescent=220, version=4, fsSelection=0x40)
    fb.setupPost()
    return fb.font


def blend(values, deltas):
    """The operands of a `blend`: the default values, then for each value its deltas (one per region of the current
    ItemVariationData), then the number of values."""
    tokens = list(values)
    for row in deltas:
        tokens += list(row)
    return tokens + [len(values), "blend"]


def write_cff2_charstrings(path):
    """Rewrites glyphs C, D, E and F of the merged CFF2 font by hand: a second and third ItemVariationData, a second Font DICT that
    FDSelect gives to E, a local subroutine in each Font DICT, and a global subroutine, none of which varLib emits by itself."""
    font = TTFont(path)
    top = font["CFF2"].cff[0]
    charstrings = top.CharStrings
    for glyph in charstrings.keys():
        charstrings[glyph].decompile()

    # Two more ItemVariationData: regions {0, 3} (thin weight, condensed width) and {2, 4} (black weight, extended width).
    store = top.VarStore.otVarStore
    for regions in ([0, 3], [2, 4]):
        data = otTables.VarData()
        data.NumShorts = 0
        data.VarRegionCount = len(regions)
        data.VarRegionIndex = regions
        data.Item = []
        data.ItemCount = 0
        store.VarData.append(data)
    store.VarDataCount = len(store.VarData)

    # A second Font DICT whose Private DICT names ItemVariationData 2, and the FDSelect that gives it glyph E.
    fd0 = top.FDArray[0]
    fd1 = copy.deepcopy(fd0)
    fd1.Private.vsindex = 2
    top.FDArray.append(fd1)
    select = FDSelect()
    select.format = 3
    select.gidArray = [1 if glyph == "E" else 0 for glyph in font.getGlyphOrder()]
    top.FDSelect = select

    def subroutine(private, program):
        return T2CharString(program=program + ["return"], private=private, globalSubrs=top.GlobalSubrs)

    def local_subrs(private):
        subrs = SubrsIndex(private=private, globalSubrs=top.GlobalSubrs, fdSelect=None, fdArray=top.FDArray, isCFF2=True)
        private.Subrs = subrs
        return subrs

    # C: everything after the moveto is a local subroutine of Font DICT 0 (the bias for fewer than 1240 subroutines is 107).
    program = charstrings["C"].program
    split = next(i for i, token in enumerate(program) if token in ("hmoveto", "vmoveto", "rmoveto")) + 1
    local_subrs(fd0.Private).append(subroutine(fd0.Private, program[split:]))
    charstrings["C"].program = program[:split] + [0 - 107, "callsubr"]

    # D: `vsindex 1`, so two regions; six operands blended by one `blend`; then a curve whose operands are only partly blended.
    charstrings["D"].program = (
        [1, "vsindex"]
        + blend([120], [[-20, 15]]) + ["hmoveto"]
        + blend([300, 0, -40, 600, -260, 0], [[-20, -75], [0, 0], [5, 10], [0, 0], [15, 65], [0, 0]]) + ["rlineto"]
        + blend([40, 0], [[10, -5], [0, 0]]) + ["rmoveto"]
        + [80, 0] + blend([60, 100, 40, 0], [[8, 0], [-5, 12], [0, 0], [0, 0]]) + ["rrcurveto"]
    )

    # E: in Font DICT 1 (`vsindex 2` in its Private DICT), a local subroutine of its own that blends a curve.
    local_subrs(fd1.Private).append(subroutine(
        fd1.Private, blend([260, 0, -20, 140, 0, 0], [[30, 10], [0, 0], [-6, 4], [12, 0], [0, 0], [0, 0]]) + ["rrcurveto"]))
    charstrings["E"].private = fd1.Private
    charstrings["E"].program = blend([80, 0], [[-10, 20], [0, 0]]) + ["rmoveto", 0 - 107, "callsubr"] + [0, -200, "rlineto"]

    # F: a global subroutine (called with ItemVariationData 0, five regions) that draws two lines with blended operands.
    top.GlobalSubrs.append(T2CharString(
        program=blend([200, 0, 0, 500], [[0, 0, 0, -30, 40], [0, 0, 0, 0, 0], [0, 0, 0, 0, 0], [-10, 20, 12, 0, 25]]) + ["rlineto", "return"],
        private=None, globalSubrs=top.GlobalSubrs))
    charstrings["F"].program = blend([70], [[0, 0, 0, -18, 22]]) + ["hmoveto", 0 - 107, "callgsubr"]

    font.save(path)


def build_variable_cff2_font(workdir, vertical=False):
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
        font = build_cff_master(name, wf, wdf, vertical)
        path = os.path.join(workdir, "cff-master-%s%s.otf" % (name, "-v" if vertical else ""))
        font.save(path)
        source = SourceDescriptor()
        source.path = path
        source.name = name
        source.location = {"Weight": design_weight, "Width": design_width}
        if name == "default":
            source.copyInfo = True
        doc.addSource(source)

    for style, wght, wdth in (("Light", 250, 100), ("Bold", 600, 100)):
        instance = InstanceDescriptor()
        instance.familyName = "Variable Cff2 Test"
        instance.styleName = style
        instance.location = {"Weight": wght, "Width": wdth}
        doc.addInstance(instance)

    variable, _, _ = varlib_build(doc)
    merged = os.path.join(workdir, "merged-v.otf" if vertical else "merged.otf")
    variable.save(merged)
    write_cff2_charstrings(merged)
    return TTFont(merged)


def cff2_oracle_font(path):
    """The CFF2 font with glyph E's `vsindex` written into its charstring instead of its Private DICT. The two mean the same, but
    fontTools' instancer cannot instantiate a Private DICT that has a `vsindex` (it indexes the integer), so the reference values
    are made from this copy while the fixture itself keeps the Private DICT entry."""
    font = TTFont(path)
    top = font["CFF2"].cff[0]
    private = top.FDArray[1].Private
    index = private.vsindex
    del private.vsindex
    private.rawDict.pop("vsindex", None)  # the attribute is decoded again from here when it is missing
    charstring = top.CharStrings["E"]
    charstring.decompile()
    charstring.program = [index, "vsindex"] + charstring.program
    return font


def describe_cff2(font):
    """Every glyph's outline as fontTools' pen draws the instantiated charstring (absolute coordinates, contours closed
    implicitly), and its advance."""
    glyph_set = font.getGlyphSet()
    hmtx = font["hmtx"]
    glyphs = {}
    for glyph in font.getGlyphOrder():
        pen = RecordingPen()
        glyph_set[glyph].draw(pen)
        contours = []
        for op, args in pen.value:
            if op == "moveTo":
                contours.append([["M", float(args[0][0]), float(args[0][1])]])
            elif op == "lineTo":
                contours[-1].append(["L", float(args[0][0]), float(args[0][1])])
            elif op == "curveTo":
                contours[-1].append(["C"] + [float(v) for point in args for v in point])
        glyphs[glyph] = {"advance": hmtx[glyph][0], "outline": contours}
    return {"glyphs": glyphs}


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

    with tempfile.TemporaryDirectory() as workdir:
        build_variable_cff2_font(workdir).save(OUT_CFF2_FONT)

    cff2_tags = sorted(TTFont(OUT_CFF2_FONT).keys())
    assert {"CFF2", "fvar", "avar", "HVAR"} <= set(cff2_tags) and "glyf" not in cff2_tags, cff2_tags

    cff2_golden = {"locations": []}
    for loc in LOCATIONS:
        instance = instancer.instantiateVariableFont(cff2_oracle_font(OUT_CFF2_FONT), dict(loc), inplace=False)
        entry = describe_cff2(instance)
        entry["location"] = loc
        cff2_golden["locations"].append(entry)

    with open(OUT_CFF2_GOLDEN, "w", encoding="utf-8", newline="\n") as f:
        json.dump(cff2_golden, f, indent=1)
        f.write("\n")
    print("tables:", " ".join(cff2_tags))
    print("wrote", OUT_CFF2_FONT, os.path.getsize(OUT_CFF2_FONT), "bytes and", OUT_CFF2_GOLDEN)

    write_vertical_fixtures()
    write_avar2_fixture()


def saved_copy(font):
    """The font written out and read back, so that the tables fontTools recalculates when it saves (the `head` box) hold their final values."""
    buffer = io.BytesIO()
    font.save(buffer)
    buffer.seek(0)
    return TTFont(buffer)


def box_of(font):
    head = saved_copy(font)["head"]
    return [head.xMin, head.yMin, head.xMax, head.yMax]


def vertical_entry(instance, box_source):
    vmtx = instance["vmtx"]
    return {
        "advances": {gn: vmtx[gn][0] for gn in instance.getGlyphOrder()},
        "ascent": instance["vhea"].ascent,
        "box": box_of(box_source),
    }


def cff2_vertical_origins(variable, location):
    """The vertical origin of every glyph at `location`, from the VORG table and the vertical origin mapping of VVAR, evaluated with
    fontTools' own ItemVariationStore code (its instancer does not update VORG)."""
    axes = variable["fvar"].axes
    normalized = normalizeLocation(location, {a.axisTag: (a.minValue, a.defaultValue, a.maxValue) for a in axes})
    normalized = variable["avar"].renormalizeLocation(normalized, variable)
    vvar = variable["VVAR"].table
    store = VarStoreInstancer(vvar.VarStore, axes, normalized)
    vorg = variable["VORG"]
    origins = {}
    for gn in variable.getGlyphOrder():
        base = vorg.VOriginRecords.get(gn, vorg.defaultVertOriginY)
        origins[gn] = base + otRound(store[vvar.VOrgMap.mapping[gn]])
    return origins


def write_vertical_fixtures():
    """The vertical-metrics fixtures: a TrueType and a CFF2 font whose vertical advances (and, for CFF2, vertical origins) vary, and the
    values fontTools' instancer gives at the same locations, together with the font bounding box the instance is saved with."""
    with tempfile.TemporaryDirectory() as workdir:
        build_variable_font(workdir, vertical=True).save(OUT_VERTICAL_FONT)

    without_vvar = TTFont(OUT_VERTICAL_FONT)
    del without_vvar["VVAR"]
    without_vvar.save(OUT_VERTICAL_FONT_NO_VVAR)
    tags = sorted(TTFont(OUT_VERTICAL_FONT).keys())
    assert {"vhea", "vmtx", "VVAR", "gvar", "MVAR"} <= set(tags), tags

    golden = {"locations": []}
    for loc in LOCATIONS:
        instance = instancer.instantiateVariableFont(TTFont(OUT_VERTICAL_FONT), dict(loc), inplace=False)
        golden["locations"].append({"location": loc, **vertical_entry(instance, instance)})
    write_json(OUT_VERTICAL_GOLDEN, golden)
    print("wrote", OUT_VERTICAL_FONT, os.path.getsize(OUT_VERTICAL_FONT), "bytes and", OUT_VERTICAL_GOLDEN)

    with tempfile.TemporaryDirectory() as workdir:
        build_variable_cff2_font(workdir, vertical=True).save(OUT_CFF2_VERTICAL_FONT)

    variable = TTFont(OUT_CFF2_VERTICAL_FONT)
    assert {"CFF2", "vhea", "vmtx", "VORG", "VVAR"} <= set(variable.keys()), sorted(variable.keys())
    golden = {"locations": []}
    for loc in LOCATIONS:
        instance = instancer.instantiateVariableFont(cff2_oracle_font(OUT_CFF2_VERTICAL_FONT), dict(loc), inplace=False)
        entry = vertical_entry(instance, instance)
        entry["origins"] = cff2_vertical_origins(variable, loc)
        golden["locations"].append({"location": loc, **entry})
    write_json(OUT_CFF2_VERTICAL_GOLDEN, golden)
    print("wrote", OUT_CFF2_VERTICAL_FONT, os.path.getsize(OUT_CFF2_VERTICAL_FONT), "bytes and", OUT_CFF2_VERTICAL_GOLDEN)


def write_json(path, value):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(value, f, indent=1)
        f.write("\n")


# Locations for the avar 2 font, in user coordinates: the segment map, the cross-axis mapping (weight reaching width and width reaching weight)
# and the clamps all matter.
AVAR2_LOCATIONS = [
    {"wght": 400, "wdth": 100},
    {"wght": 700, "wdth": 100},
    {"wght": 900, "wdth": 100},
    {"wght": 900, "wdth": 75},
    {"wght": 600, "wdth": 90},
    {"wght": 250, "wdth": 80},
    {"wght": 400, "wdth": 75},
    {"wght": 700, "wdth": 120},
    {"wght": 1000, "wdth": 30},
]


def build_avar2_font():
    """VariableTest.ttf with an `avar` version 2 table: the version 1 segment maps as they are, and an ItemVariationStore that moves the
    normalized width by an amount that depends on the (mapped) weight, and the weight by an amount that depends on the width. The axis
    index map sends the axes to the items in the opposite order, so that the map is exercised."""
    font = TTFont(OUT_FONT)
    axis_tags = ["wght", "wdth"]
    supports = [{"wght": (0.0, 1.0, 1.0)}, {"wdth": (-1.0, -1.0, 0.0)}, {"wght": (-1.0, -1.0, 0.0), "wdth": (0.0, 1.0, 1.0)}]
    regions = builder.buildVarRegionList(supports, axis_tags)
    # Item 0 changes the width, item 1 the weight (in units of 1/16384): full weight narrows the width by a quarter of its range; a narrow
    # width makes the weight heavier by an eighth; a light weight with a wide width lowers the weight further.
    data = builder.buildVarData([0, 1, 2], [[-4096, 0, 0], [0, 2048, -1024]], optimize=False)
    avar = font["avar"]
    avar.majorVersion, avar.minorVersion = 2, 0
    avar.table = otTables.avar()
    avar.table.Reserved = 0
    avar.table.VarStore = builder.buildVarStore(regions, [data])
    index_map = otTables.DeltaSetIndexMap()
    index_map.Format = 0
    index_map.mapping = [1, 0]      # the weight is item 1, the width is item 0
    avar.table.VarIdxMap = index_map
    return font


def write_avar2_fixture():
    font = build_avar2_font()
    font.save(OUT_AVAR2_FONT)
    variable = TTFont(OUT_AVAR2_FONT)
    assert variable["avar"].majorVersion == 2

    axes = variable["fvar"].axes
    limits = {a.axisTag: (a.minValue, a.defaultValue, a.maxValue) for a in axes}
    golden = {"locations": []}
    for loc in AVAR2_LOCATIONS:
        normalized = normalizeLocation(loc, limits)
        mapped = variable["avar"].renormalizeLocation(normalized, variable, dropZeroes=False)

        # The oracle: the same font without any avar, at the user values that normalize to the mapped location.
        plain = TTFont(OUT_FONT)
        del plain["avar"]
        user = {}
        for tag, n in mapped.items():
            low, default, high = limits[tag]
            user[tag] = default + n * (high - default) if n >= 0 else default + n * (default - low)
        instance = instancer.instantiateVariableFont(plain, user, inplace=False)
        entry = describe(instance)
        entry["location"] = loc
        entry["normalized"] = mapped
        golden["locations"].append(entry)

    write_json(OUT_AVAR2_GOLDEN, golden)
    print("wrote", OUT_AVAR2_FONT, os.path.getsize(OUT_AVAR2_FONT), "bytes and", OUT_AVAR2_GOLDEN)


if __name__ == "__main__":
    main()
