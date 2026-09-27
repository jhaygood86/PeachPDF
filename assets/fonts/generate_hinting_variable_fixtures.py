#!/usr/bin/env python3
"""Generates the fixtures of the variable-font TrueType hinting tests and their references, HintingVariable.golden.json.gz and
HintingVariableVariants.golden.json.gz: what FreeType itself makes of every glyph of a variable TrueType font at many locations of its design space,
for the port of FreeType's variation code (PeachDrawing.Text/Internal/Hinting/FreeType/TtVar.cs, and the way TtGload.cs and TtFace.cs use it) to be compared
with exactly, in 26.6 units, refusals included.

A variable TrueType font moves the points of every glyph by the deltas of its `gvar` table, and the control values of its `cvt` table by those of its `cvar`
table, and FreeType does the arithmetic in 16.16 fixed point (ttgxvar.c): the normalized coordinates of a location (with the `avar` table), the scalar of
every tuple, the deltas of a glyph, the interpolation of the points a tuple leaves out, the rounding of the sum. Its `HVAR`, `VVAR` and `MVAR` tables adjust
the advances, the vertical metrics and the ranges of the `gasp` table the same way. A port that computes any of it in floating point, or rounds where FreeType
does not, hints a glyph a fraction of a pixel away from FreeType's, and this reference finds it.

The fixtures (see hinting_variable_builder.py for how their tables are written, byte by byte):

  HintingVariable.ttf          synthetic (CC0): 100 glyphs whose programs are the random programs of the opcode fixtures (every instruction, invalid arguments
                               included), 70 simple ones and 30 composites, two axes (`wght` 200 to 800 and `wdth` 75 to 150, defaults 400 and 100) with an `avar`
                               map that is not linear, a `gvar` table whose tuples are dense and sparse (so that the points they leave out are interpolated as
                               IUP would), shared and private, with intermediate regions and peaks that are not multiples of 1/16384 in 16.16, a `cvar` table,
                               an `HVAR` table with a delta-set index map, an `MVAR` table that moves the `gasp` ranges and the typographic ascender and
                               descender, a `gasp` table; no vertical metrics
  HintingVariableNoHvar.ttf    the same without `HVAR`: the advances follow the phantom points of `gvar`
  HintingVariableVertical.ttf  the same with `vhea`, `vmtx` and a `VVAR` table (and an `HVAR` table without a mapping)
  HintingVariableAvar2.ttf     the same with an `avar` table of version 2: a cross-axis mapping by an item variation store
  the bundled variable fonts   VariableTest.ttf, VariableCvarTest.ttf, ... the real ones (their glyphs have no programs, so they show the arithmetic of the
                               deltas and of scaling more than the interpreter)

Each is recorded at several locations: at design coordinates FreeType itself normalizes (which the API can reach: multiples of 1/64) and at raw normalized
coordinates that are not multiples of 1/16384 (which it cannot, but the engine can be given). Each location is recorded with the normalized vector FreeType
kept, which the tests compare with what the port makes of the design coordinates.

A second file holds fonts made wrong in one deliberate way (each variation table: its header, its offsets, its tuples, its stores), and mutants (a few bytes of
a variation table changed at random), with what FreeType makes of them: whether it can set the location, and a digest of every glyph; the port has to do
what FreeType does.

Usage (see generate_hinting_golden.py for --freetype):

  python generate_hinting_variable_fixtures.py --freetype path/to/freetype.dll

Requires freetype-py and fontTools.
"""
import argparse
import ctypes
import gzip
import importlib.util
import io
import json
import math
import os
import random
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import hinting_cff2_builder as cb  # noqa: E402  (replace_table, table_of)
import hinting_variable_builder as vb  # noqa: E402

_spec = importlib.util.spec_from_file_location("hinting_opcodes", os.path.join(HERE, "generate_hinting_opcode_fixtures.py"))
ops = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ops)

OUT_GOLDEN = os.path.join(HERE, "HintingVariable.golden.json.gz")
OUT_VARIANTS = os.path.join(HERE, "HintingVariableVariants.golden.json.gz")

SEED = 0x7A61
SIMPLE_GLYPHS = 70
COMPOSITE_GLYPHS = 30
CVT_COUNT = ops.CVT_COUNT

# 26.6 sizes recorded, and the modes: standard is interpreter version 40 (FreeType's default), monochrome version 35 with the mono target (see generate_hinting_golden.py)
SIZES = [9 * 64, 13 * 64, 24 * 64]
MODES = {
    "standard": (40, False),
    "monochrome": (35, True),
}

FT_LOAD_NO_BITMAP = 0x8
FT_LOAD_NO_AUTOHINT = 0x8000
FT_LOAD_TARGET_MONO = 2 << 16

AXES = [("wght", 200, 400, 800), ("wdth", 75, 100, 150)]
# a map for the weight that is not linear and not exact in 2.14 (0.4 -> 0.65 is 6553.6/16384 -> 10649.6/16384), and one for the width
AVAR = [
    [(-1.0, -1.0), (-0.5, -0.3), (0.0, 0.0), (0.4, 0.65), (1.0, 1.0)],
    [(-1.0, -1.0), (0.0, 0.0), (0.6, 0.5), (1.0, 1.0)],
]

BUNDLED = [
    "VariableTest.ttf", "VariableTestNoHvar.ttf", "VariableAvar2Test.ttf", "VariableCvarTest.ttf", "VariableSlantTest.ttf",
    "VariableColorTest.ttf", "VariableFeatureTest.ttf", "VariableLayoutTest.ttf", "VariableVerticalTest.ttf", "VariableVerticalTestNoVvar.ttf",
]


# ---- the regions of the variation ----------------------------------------------------------------------------------------------

def region_menu(rng, count):
    """Regions of the design space (axis tag: (start, peak, end)) that tuples and stores are made from: some on one axis and some on both, at the ends and
    inside, some intermediate, some with peaks that no 2.14 number is exact for in 16.16."""
    peaks = [1.0, -1.0, 1.0, -1.0, 0.5, -0.5, 0.3, -0.7, 0.75, -0.25, 0.9, 0.6]
    # the ends of each axis and of both together come first (and are picked most), so that every extreme location moves many glyphs
    menu = [{"wght": (0.0, 1.0, 1.0)}, {"wght": (-1.0, -1.0, 0.0)}, {"wdth": (0.0, 1.0, 1.0)}, {"wdth": (-1.0, -1.0, 0.0)},
            {"wght": (0.0, 1.0, 1.0), "wdth": (0.0, 1.0, 1.0)}, {"wght": (-1.0, -1.0, 0.0), "wdth": (-1.0, -1.0, 0.0)}]
    while len(menu) < count:
        tags = [t for t in ("wght", "wdth") if rng.random() < 0.6] or [rng.choice(("wght", "wdth"))]
        region = {}
        for tag in tags:
            peak = rng.choice(peaks)
            if rng.random() < 0.3:
                # an intermediate region: it may not straddle the default
                if peak > 0:
                    start = rng.choice([0.0, 0.0, 0.1, round(peak * 0.4, 4)])
                    end = rng.choice([1.0, min(1.0, round(peak + 0.3, 4))])
                    region[tag] = (start, peak, max(end, peak))
                else:
                    end = rng.choice([0.0, 0.0, -0.1, round(peak * 0.4, 4)])
                    start = rng.choice([-1.0, max(-1.0, round(peak - 0.3, 4))])
                    region[tag] = (min(start, peak), peak, end)
            else:
                region[tag] = (0.0, peak, peak) if peak > 0 else (peak, peak, 0.0)
        if region not in menu:
            menu.append(region)
    return menu


def store_region(region):
    """A region of the menu as the (start, peak, end) of every axis, for an item variation store."""
    return [region.get(tag, (0.0, 0.0, 0.0)) for tag, *_ in AXES]


def delta(rng):
    c = rng.random()
    if c < 0.6:
        return rng.randint(-25, 25)
    if c < 0.9:
        return rng.randint(-90, 90)
    return rng.randint(-400, 400)


def tuple_coordinates(rng, count, phantom_start=None):
    """The deltas of one tuple for `count` points: all of them, or some (the others are None: the points a tuple leaves out, which are interpolated)."""
    coords = [None] * count
    if rng.random() < 0.35:
        chosen = range(count)
    else:
        chosen = [i for i in range(count) if rng.random() < rng.choice([0.25, 0.5, 0.8])]
        if not chosen:
            chosen = [rng.randrange(count)]
    for i in chosen:
        coords[i] = (delta(rng), delta(rng))
    # the phantom points are moved by few tuples
    if phantom_start is not None:
        for i in range(phantom_start, count):
            if rng.random() < 0.6:
                coords[i] = None if rng.random() < 0.5 else (rng.randint(-40, 40), rng.randint(-40, 40))
    if all(c is None for c in coords):
        coords[0] = (delta(rng), delta(rng))
    return coords


# ---- the font -------------------------------------------------------------------------------------------------------------------

def build_base_font():
    """HintingVariable.ttf without the tables that are built by hand (HVAR, VVAR, MVAR, avar 2, gasp, vertical metrics): returns the fontTools font."""
    from array import array

    from fontTools.fontBuilder import FontBuilder
    from fontTools.pens.ttGlyphPen import TTGlyphPen
    from fontTools.ttLib import newTable
    from fontTools.ttLib.tables import ttProgram
    from fontTools.ttLib.tables._g_l_y_f import Glyph, GlyphComponent
    from fontTools.ttLib.tables.TupleVariation import TupleVariation

    rng = random.Random(SEED)
    simple = ["s%d" % i for i in range(1, SIMPLE_GLYPHS + 1)]
    composite = ["c%d" % i for i in range(1, COMPOSITE_GLYPHS + 1)]
    names = [".notdef"] + simple + composite
    fb = FontBuilder(1000, isTTF=True)
    fb.setupGlyphOrder(names)
    fb.setupCharacterMap({0x21 + i: names[i + 1] for i in range(100)})

    fpgm_bytes, functions, idefs = ops.build_font_program()
    menu = region_menu(rng, 14)

    glyphs = {}
    metrics = {}
    point_counts = {}
    contour_counts = {}
    gvar_variations = {}

    def variations_for(name, count, phantom_start):
        tuples = []
        for _ in range(rng.randrange(3, 9)):
            region = rng.choice(menu[:6]) if rng.random() < 0.5 else rng.choice(menu)
            tuples.append(TupleVariation(dict(region), tuple_coordinates(rng, count, phantom_start)))
        gvar_variations[name] = tuples

    for i, name in enumerate([".notdef"] + simple):
        empty = name != ".notdef" and i % 17 == 0
        contours = [] if empty else ops.glyph_outline(rng)
        pen = TTGlyphPen(None)
        for pts in contours:
            first_on = next(k for k, p in enumerate(pts) if p[2])
            ordered = pts[first_on:] + pts[:first_on]
            pen.moveTo((ordered[0][0], ordered[0][1]))
            pending = []
            for x, y, on in ordered[1:]:
                if on:
                    if pending:
                        pen.qCurveTo(*pending, (x, y))
                        pending = []
                    else:
                        pen.lineTo((x, y))
                else:
                    pending.append((x, y))
            if pending:
                pen.qCurveTo(*pending, (ordered[0][0], ordered[0][1]))
            pen.closePath()
        glyph = pen.glyph()
        n_points = len(glyph.coordinates) if contours else 0
        point_counts[name] = n_points
        contour_counts[name] = len(contours)
        if contours:
            gen = ops.Gen(rng, max(n_points, 1), len(contours), functions, idefs)
            prog = ttProgram.Program()
            prog.fromBytecode(gen.forced(i) + gen.program())
            glyph.program = prog
        glyphs[name] = glyph
        metrics[name] = (rng.randrange(300, 900), 0)
        # every glyph varies, in a few tuples; an empty one only in its phantom points
        variations_for(name, n_points + 4, n_points if contours else 0)

    for k, name in enumerate(composite):
        g = Glyph()
        g.numberOfContours = -1
        comps = []
        total_points = 0
        pool = simple if k < COMPOSITE_GLYPHS // 2 else simple + composite[:k]
        pool = [p for p in pool if point_counts.get(p, 0) > 0]
        for c in range(rng.randrange(1, 4)):
            comp = GlyphComponent()
            comp.glyphName = rng.choice(pool)
            pts = point_counts.get(comp.glyphName, 20)
            total_points += pts
            if c > 0 and rng.random() < 0.3 and total_points > pts:
                comp.firstPt = rng.randrange(max(total_points - pts, 1))
                comp.secondPt = rng.randrange(max(pts, 1))
                comp.flags = rng.choice([0, ops.ROUND_XY_TO_GRID])
            else:
                comp.x = rng.choice([0, rng.randrange(-120, 120), rng.randrange(-400, 400), rng.randrange(-40, 40)])
                comp.y = rng.choice([0, rng.randrange(-120, 120), rng.randrange(-400, 400), rng.randrange(-40, 40)])
                comp.flags = rng.choice([0, ops.ROUND_XY_TO_GRID, ops.ROUND_XY_TO_GRID, ops.SCALED_COMPONENT_OFFSET, ops.UNSCALED_COMPONENT_OFFSET,
                                         ops.SCALED_COMPONENT_OFFSET | ops.ROUND_XY_TO_GRID])
            if rng.random() < 0.3:
                comp.flags |= ops.USE_MY_METRICS
            form = rng.randrange(4)
            if form == 1:
                s = rng.uniform(0.3, 1.9)
                comp.transform = [[s, 0], [0, s]]
            elif form == 2:
                comp.transform = [[rng.uniform(0.3, 1.9), 0], [0, rng.uniform(0.3, 1.9)]]
            elif form == 3:
                comp.transform = [[rng.uniform(-1, 1), rng.uniform(-0.6, 0.6)], [rng.uniform(-0.6, 0.6), rng.uniform(-1, 1)]]
            comps.append(comp)
        g.components = comps
        gen = ops.Gen(rng, max(total_points, 1), 3, functions, idefs)
        prog = ttProgram.Program()
        prog.fromBytecode(gen.forced(k) + gen.program() if rng.random() < 0.85 else b"")
        g.program = prog
        glyphs[name] = g
        metrics[name] = (rng.randrange(300, 900), 0)
        point_counts[name] = total_points
        # the deltas of a composite are the offsets of its components, then the phantom points
        variations_for(name, len(comps) + 4, None)

    fb.setupGlyf(glyphs)
    fb.setupHorizontalMetrics(metrics)
    fb.setupHorizontalHeader(ascent=800, descent=-200)
    fb.setupNameTable({"familyName": "HintingVariable", "styleName": "Regular"})
    fb.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
    fb.setupPost()
    fb.setupFvar([(tag, mn, df, mx, tag) for tag, mn, df, mx in AXES], [])

    font = fb.font
    font.recalcBBoxes = False
    font.recalcTimestamp = False
    font["head"].created = font["head"].modified = 3786825600

    fpgm = newTable("fpgm")
    fpgm.program = ttProgram.Program()
    fpgm.program.fromBytecode(fpgm_bytes)
    font["fpgm"] = fpgm

    prep = newTable("prep")
    prep.program = ttProgram.Program()
    prep.program.fromBytecode(ops.build_cvt_program(rng))
    font["prep"] = prep

    cvt = newTable("cvt ")
    cvt.values = array("h", [rng.randrange(-40, 700) for _ in range(CVT_COUNT)])
    font["cvt "] = cvt

    maxp = font["maxp"]
    maxp.maxZones = 2
    maxp.maxTwilightPoints = ops.TWILIGHT
    maxp.maxStorage = ops.STORE_COUNT
    maxp.maxFunctionDefs = functions + 4
    maxp.maxInstructionDefs = len(idefs)
    maxp.maxStackElements = 96
    font["head"].flags |= 8

    avar = newTable("avar")
    avar.segments = {tag: {a: b for a, b in pairs} for (tag, *_), pairs in zip(AXES, AVAR)}
    font["avar"] = avar

    gvar = newTable("gvar")
    gvar.version = 1
    gvar.reserved = 0
    gvar.variations = gvar_variations
    font["gvar"] = gvar

    cvar = newTable("cvar")
    cvar.majorVersion = 1
    cvar.minorVersion = 0
    cvar_tuples = []
    for region in rng.sample(menu, 6):
        coords = [None] * CVT_COUNT
        for i in rng.sample(range(CVT_COUNT), rng.randrange(3, CVT_COUNT + 1)):
            coords[i] = rng.randint(-40, 90)
        cvar_tuples.append(TupleVariation(dict(region), coords))
    cvar.variations = cvar_tuples
    font["cvar"] = cvar

    return font, menu, len(names), rng


def store_for(rng, menu, glyph_count, want_long=False, items=None):
    """An item variation store for the advances of the glyphs (and the values of MVAR): two data sets, of regions from the menu, with words and bytes mixed."""
    regions = [store_region(r) for r in menu[:8]]
    items = items or glyph_count
    data_sets = []
    per_set = (items + 1) // 2
    for k in range(2):
        indexes = sorted(rng.sample(range(len(regions)), rng.randrange(2, 5)))
        words = rng.randrange(0, len(indexes) + 1)
        rows = []
        for _ in range(per_set):
            rows.append([rng.choice([rng.randint(-30, 30), rng.randint(-3000, 3000)]) if j < words else rng.randint(-60, 60) for j in range(len(indexes))])
        data_sets.append({"regions": indexes, "words": words, "long": want_long and k == 1, "items": rows})
    return vb.item_variation_store(len(AXES), regions, data_sets), per_set


def flavors(base_bytes, menu, glyph_count, rng):
    """The four fonts: the tables that are built by hand added to (or left out of) the font fontTools made."""
    # one store for all of them, so that the fonts differ in what they use
    hvar_store, per_set = store_for(rng, menu, glyph_count)
    entries = [(g // per_set, g % per_set) for g in range(glyph_count)]
    width_map = vb.delta_set_index_map(entries, fmt=0, inner_bits=6, entry_size=2)
    hvar = vb.hvar_table(hvar_store, width_map)
    hvar_plain = vb.hvar_table(store_for(rng, menu, glyph_count)[0], None)

    mvar_store, _ = store_for(rng, menu, 8, want_long=True)
    mvar = vb.mvar_table(mvar_store, [("hasc", 0, 1), ("hdsc", 0, 2), ("gsp0", 0, 3), ("gsp1", 1, 0), ("gsp2", 1, 1), ("zzzz", 0, 0), ("cpht", 1, 2)])
    gasp = vb.gasp_table([(8, 2), (14, 3), (20, 1), (0xFFFF, 3)])

    def with_tables(font, extra):
        for tag, table in extra.items():
            font = cb.replace_table(font, tag, table)
        return font

    main = with_tables(base_bytes, {"HVAR": hvar, "MVAR": mvar, "gasp": gasp})

    no_hvar = with_tables(base_bytes, {"MVAR": mvar, "gasp": gasp})

    # vertical metrics: vhea and vmtx, and VVAR (with its own mapping); HVAR without one
    vstore, vper = store_for(rng, menu, glyph_count)
    vmap = vb.delta_set_index_map([(g // vper, g % vper) for g in range(glyph_count)], fmt=1, inner_bits=7, entry_size=2)
    vvar = vb.vvar_table(vstore, vmap)
    # 4 bytes of version, then ascent, descent, lineGap, advanceHeightMax, minTop, minBottom, yMaxExtent, caretSlopeRise, caretSlopeRun, caretOffset, 4 reserved, metricDataFormat, numOfLongVerMetrics
    vhea = struct.pack(">IhhhHhhhhhhhhhhhH", 0x00010000, 500, -500, 0, 1000, 0, 0, 1000, 1, 0, 0, 0, 0, 0, 0, 0, glyph_count)
    vmtx = b"".join(struct.pack(">Hh", 900 + (g % 7) * 20, 100 + (g % 5) * 10) for g in range(glyph_count))
    vertical = with_tables(base_bytes, {"HVAR": hvar_plain, "MVAR": mvar, "gasp": gasp, "VVAR": vvar, "vhea": vhea, "vmtx": vmtx})

    # avar version 2: the segment maps and a mapping of each axis by an item variation store over the (half-)normalized coordinates
    avar2_store = vb.item_variation_store(len(AXES), [[(0.0, 1.0, 1.0), (0.0, 0.0, 0.0)], [(-1.0, -1.0, 0.0), (0.0, 0.0, 0.0)], [(0.0, 0.0, 0.0), (0.0, 0.5, 1.0)]],
                                          [{"regions": [0, 1, 2], "words": 3, "items": [[1229, -819, 0], [0, 0, 2458], [-3000, 4000, 3000]]}])
    avar2_map = vb.delta_set_index_map([(0, 0), (0, 1)], fmt=0, inner_bits=2, entry_size=1)
    avar2 = vb.avar_table([[(a, b) for a, b in pairs] for pairs in AVAR], version=2, axis_map=avar2_map, store=avar2_store)
    avar2_font = with_tables(base_bytes, {"HVAR": hvar, "MVAR": mvar, "gasp": gasp, "avar": avar2})

    return [
        ("HintingVariable.ttf", main),
        ("HintingVariableNoHvar.ttf", no_hvar),
        ("HintingVariableVertical.ttf", vertical),
        ("HintingVariableAvar2.ttf", avar2_font),
    ]


# ---- FreeType --------------------------------------------------------------------------------------------------------------------

def load_freetype(path):
    return ops.load_freetype(path)


def fixed_array(values):
    return (ctypes.c_long * len(values))(*values)


def open_face(freetype, raw, data, interpreter):
    cbuf = ctypes.create_string_buffer(data, len(data))
    lib = freetype.FT_Library()
    assert raw.FT_Init_FreeType(ctypes.byref(lib)) == 0
    v = ctypes.c_uint(interpreter)
    assert raw._lib.FT_Property_Set(lib, b"truetype", b"interpreter-version", ctypes.byref(v)) == 0
    face = freetype.FT_Face()
    error = raw.FT_New_Memory_Face(lib, cbuf, len(data), 0, ctypes.byref(face))
    return lib, face, cbuf, error


def close_face(raw, lib, face):
    raw.FT_Done_Face(face)
    raw.FT_Done_FreeType(lib)


def axis_count(raw, freetype, data):
    """The number of axes FreeType sees in the font (0 when it is not variable)."""
    lib, face, cbuf, error = open_face(freetype, raw, data, 40)
    if error:
        raw.FT_Done_FreeType(lib)
        return 0
    count = 0
    if face.contents.face_flags & (1 << 8):  # FT_FACE_FLAG_MULTIPLE_MASTERS
        var = ctypes.c_void_p()
        if raw.FT_Get_MM_Var(face, ctypes.byref(var)) == 0:
            count = ctypes.cast(var, ctypes.POINTER(ctypes.c_uint)).contents.value
    close_face(raw, lib, face)
    return count


def axis_ranges(data):
    """The (minimum, default, maximum) of every axis of a font, from its fvar table."""
    from fontTools.ttLib import TTFont
    return [(a.minValue, a.defaultValue, a.maxValue) for a in TTFont(io.BytesIO(data))["fvar"].axes]


def set_location(raw, face, location, axes):
    """Puts the face at a location: design coordinates (which FreeType normalizes, with the avar table) or raw normalized ones. Returns (FreeType's error, the
    normalized vector it kept or None). `axes` is the number of axes FreeType sees in the font; a font it sees none in gets the call anyway, to record its answer."""
    if location.get("design") is not None:
        values = [int(round(v * 65536)) for v in location["design"]]
        error = raw.FT_Set_Var_Design_Coordinates(face, len(values), fixed_array(values))
    elif location.get("blend") is not None:
        blend = location["blend"]
        error = raw.FT_Set_Var_Blend_Coordinates(face, len(blend), fixed_array(blend))
    else:
        # a face nothing was set on: asking it for its coordinates would make FreeType select the default instance (and vary the control values by the cvar
        # table, which a face that is left alone never does), so it is not asked
        return 0, None
    if error or axes == 0:
        return error, None
    out = fixed_array([0] * axes)
    if raw.FT_Get_Var_Blend_Coordinates(face, axes, out) != 0:
        return 0, None
    return 0, [int(v) for v in out]


def glyph_record(raw, face, gid, flags):
    error = raw.FT_Load_Glyph(face, gid, flags)
    if error:
        return {"err": error}
    slot = face.contents.glyph.contents
    outline = slot.outline
    n = outline.n_points & 0xFFFF
    return {
        "a": slot.advance.x,
        "e": [outline.contours[i] & 0xFFFF for i in range(outline.n_contours & 0xFFFF)],
        "x": [outline.points[i].x for i in range(n)],
        "y": [outline.points[i].y for i in range(n)],
        "t": [outline.tags[i] & 1 for i in range(n)],
    }


def digest(record):
    """A 64-bit FNV-1a digest of a glyph record: the advance, the contour ends and the points (the same as the tests compute for the port), as 16 hex digits."""
    if "err" in record:
        return "e%d" % record["err"]
    numbers = [record["a"], len(record["e"])] + record["e"] + [len(record["x"])] + record["x"] + record["y"] + record["t"]
    h = 0xCBF29CE484222325
    for v in numbers:
        for b in struct.pack("<i", v):
            h = ((h ^ b) * 0x100000001B3) & 0xFFFFFFFFFFFFFFFF
    return "%016x" % h


def gasp_flags(raw, face, ppems):
    fn = raw._lib.FT_Get_Gasp
    fn.restype = ctypes.c_int
    fn.argtypes = [ctypes.c_void_p, ctypes.c_uint]
    return [int(fn(face, p)) for p in ppems]


# where the recorded locations are: a fraction of each axis's range (positive: from the default towards the maximum, negative: towards the minimum).
# Design coordinates are rounded to 1/64 (which is what the API keeps) and most fractions are not dyadic, so that FreeType's 16.16 normalization and the
# package's 2.14 one differ.
FRACTIONS = [
    (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1), (0.5, 0), (-0.5, 0), (0.5, 0.5), (0.3, 0), (-0.7, 0), (0.81, 0.33), (-0.45, 0.12),
    (0.25, -0.6), (0.9, -0.9), (0.05, 0.05), (0.62, 0.62), (-0.2, 0.4), (0.7, -0.15),
]
RAW = [[19661, -45875], [53084, 21627], [-29491, 7864], [655, 655], [65535, 1]]


def design_value(axis, fraction):
    minimum, default, maximum = axis
    value = default + fraction * (maximum - default if fraction > 0 else default - minimum)
    return round(value * 64) / 64


def locations_for(axes, ranges):
    # the first is a face nothing is set on, which has no blend at all; the design coordinates of the defaults are a face that was set to them, which has one
    out = [{"name": "as opened", "design": None, "blend": None}]
    for fractions in FRACTIONS:
        design = [design_value(ranges[i], fractions[i] if i < 2 else 0) for i in range(axes)]
        out.append({"name": "design %s" % (fractions,), "design": design, "blend": None})
    for blend in RAW:
        out.append({"name": "raw %s" % (blend,), "design": None, "blend": (blend + [0] * axes)[:axes]})
    return out


def record_font(freetype, raw, file_name, data, glyphs, sizes=SIZES, modes=MODES, with_gasp=False):
    axes = axis_count(raw, freetype, data)
    assert axes > 0, file_name
    ranges = axis_ranges(data)
    locations = locations_for(axes, ranges)
    entry = {"file": file_name, "axes": axes, "glyphs": glyphs, "locations": [], "modes": {m: [] for m in modes}}

    for li, location in enumerate(locations):
        lib, face, cbuf, error = open_face(freetype, raw, data, 40)
        assert error == 0
        err, ndv = set_location(raw, face, location, axes)
        loc = {"name": location["name"], "design": location["design"], "blend": location["blend"], "set": err, "ndv": ndv}
        if with_gasp and not err:
            loc["gasp"] = gasp_flags(raw, face, range(0, 41))
        entry["locations"].append(loc)
        close_face(raw, lib, face)

        if err:
            continue

        for mode_name, (interpreter, mono) in modes.items():
            lib, face, cbuf, error = open_face(freetype, raw, data, interpreter)
            assert error == 0
            assert set_location(raw, face, location, axes)[0] == 0
            flags = FT_LOAD_NO_BITMAP | FT_LOAD_NO_AUTOHINT | (FT_LOAD_TARGET_MONO if mono else 0)
            for size in sizes:
                per_glyph = {}
                for gid in glyphs:
                    # the size is requested again for each glyph so that FreeType runs the CVT program afresh (see generate_hinting_golden.py)
                    assert raw.FT_Set_Char_Size(face, size, size, 72, 72) == 0
                    per_glyph[str(gid)] = glyph_record(raw, face, gid, flags)
                entry["modes"][mode_name].append({"size": size, "loc": li, "glyphs": per_glyph})
            close_face(raw, lib, face)
    return entry


def report(entry):
    loaded = refused = 0
    for runs in entry["modes"].values():
        for run in runs:
            for g in run["glyphs"].values():
                if "err" in g:
                    refused += 1
                else:
                    loaded += 1
    unset = sum(1 for loc in entry["locations"] if loc["set"])
    print("  %s: %d loaded, %d refused, %d locations that cannot be set" % (entry["file"], loaded, refused, unset))


# ---- fonts that are wrong ---------------------------------------------------------------------------------------------------------

def table_bytes(font, tag):
    return bytes(cb.table_of(font, tag))


def edit(font, tag, fn):
    """The table `tag` of the font after `fn` changed it (in place, or by returning the new bytes)."""
    table = bytearray(table_bytes(font, tag))
    result = fn(table)
    if result is not None:
        table = result
    return bytes(table)


def with_table(font, tag, table):
    """The font with a table replaced, or (with None) left out."""
    return remove(font, tag) if table is None else cb.replace_table(font, tag, table)


def remove(font, tag):
    from fontTools.ttLib.sfnt import SFNTReader, SFNTWriter
    reader = SFNTReader(io.BytesIO(font))
    tables = {t: reader[t] for t in reader.tables if t != tag}
    out = io.BytesIO()
    writer = SFNTWriter(out, len(tables), reader.sfntVersion)
    for t in sorted(tables):
        writer[t] = tables[t]
    writer.close()
    return out.getvalue()


def put16(table, at, value):
    table[at:at + 2] = struct.pack(">H", value & 0xFFFF)


def put32(table, at, value):
    table[at:at + 4] = struct.pack(">I", value & 0xFFFFFFFF)


def get16(table, at):
    return struct.unpack_from(">H", table, at)[0]


def get32(table, at):
    return struct.unpack_from(">I", table, at)[0]


def gvar_long_offsets(table):
    """The gvar table with 32-bit offsets (a valid form that fontTools writes only for big tables)."""
    count = get16(table, 12)
    flags = get16(table, 14)
    assert flags & 1 == 0
    offsets = [get16(table, 20 + 2 * i) * 2 for i in range(count + 1)]
    data_offset = get32(table, 16)
    tail = table[20 + 2 * (count + 1):]
    shared_count = get16(table, 6)
    new_header = bytearray(table[:20])
    put16(new_header, 14, flags | 1)
    added = 2 * (count + 1)
    put32(new_header, 8, get32(table, 8) + added)
    put32(new_header, 16, data_offset + added)
    return bytes(new_header) + b"".join(struct.pack(">I", o) for o in offsets) + bytes(tail)


def a_glyph_with_data(table):
    """The offset (in the table) and size of the first glyph of the gvar table that has variation data, and its glyph index."""
    count = get16(table, 12)
    data_offset = get32(table, 16)
    for g in range(count):
        a, b = get16(table, 20 + 2 * g) * 2, get16(table, 20 + 2 * (g + 1)) * 2
        if b - a >= 16:
            return g, data_offset + a, b - a
    raise AssertionError("no glyph has data")


def variants_of(base):
    """(name, table, its new bytes or None to leave it out) for the base font, each made wrong in one deliberate way (or right in a way the base is not)."""
    out = [("baseline", "head", table_bytes(base, "head"))]

    def add(name, tag, fn):
        out.append((name, tag, edit(base, tag, fn)))

    def gv(name, fn):
        add(name, "gvar", fn)

    def cv(name, fn):
        add(name, "cvar", fn)

    def av(name, fn):
        add(name, "avar", fn)

    def hv(name, fn):
        add(name, "HVAR", fn)

    def mv(name, fn):
        add(name, "MVAR", fn)

    def fv(name, fn):
        add(name, "fvar", fn)

    # gvar
    gv("gvar version 2", lambda t: put32(t, 0, 0x00020000))
    gv("gvar axis count 3", lambda t: put16(t, 4, 3))
    gv("gvar glyph count 65535", lambda t: put16(t, 12, 0xFFFF))
    gv("gvar glyph count 5", lambda t: put16(t, 12, 5))
    gv("gvar shared tuple count 4000", lambda t: put16(t, 6, 4000))
    gv("gvar shared tuples offset past the table", lambda t: put32(t, 8, len(t) + 100))
    gv("gvar data offset past the table", lambda t: put32(t, 16, len(t) + 100))
    gv("gvar data offset 0", lambda t: put32(t, 16, 0))
    gv("gvar long offsets", lambda t: gvar_long_offsets(bytes(t)))
    gv("gvar long offsets flag on short offsets", lambda t: put16(t, 14, get16(t, 14) | 1))
    gv("gvar truncated", lambda t: bytearray(t[:len(t) - 60]))
    gv("gvar truncated to the header", lambda t: bytearray(t[:20]))
    gv("gvar too short for the header", lambda t: bytearray(t[:12]))
    out.append(("gvar missing", "gvar", None))

    def not_monotonic(t):
        put16(t, 20 + 2 * 10, get16(t, 20 + 2 * 12))
        put16(t, 20 + 2 * 20, 3)

    gv("gvar offsets not monotonic", not_monotonic)
    gv("gvar an offset past the table", lambda t: put16(t, 20 + 2 * 15, 0xFFFF))

    def glyph_patch(what):
        def patch(t):
            g, at, size = a_glyph_with_data(t)
            what(t, at, size)
        return patch

    gv("gvar glyph tuple count 4095", glyph_patch(lambda t, at, size: put16(t, at, 0x0FFF | (get16(t, at) & 0xF000))))
    gv("gvar glyph data offset past the data", glyph_patch(lambda t, at, size: put16(t, at + 2, size + 10)))
    gv("gvar glyph data offset 0", glyph_patch(lambda t, at, size: put16(t, at + 2, 0)))
    gv("gvar glyph shared points flag off", glyph_patch(lambda t, at, size: put16(t, at, get16(t, at) & 0x7FFF)))
    gv("gvar glyph shared points flag on", glyph_patch(lambda t, at, size: put16(t, at, get16(t, at) | 0x8000)))
    gv("gvar glyph first tuple index past the shared tuples", glyph_patch(lambda t, at, size: put16(t, at + 6, (get16(t, at + 6) & 0xF000) | 0x0FF0)))
    gv("gvar glyph first tuple data size 0", glyph_patch(lambda t, at, size: put16(t, at + 4, 0)))
    gv("gvar glyph first tuple data size 65535", glyph_patch(lambda t, at, size: put16(t, at + 4, 0xFFFF)))
    gv("gvar glyph first tuple private points flag toggled", glyph_patch(lambda t, at, size: put16(t, at + 6, get16(t, at + 6) ^ 0x2000)))
    gv("gvar glyph first tuple intermediate flag toggled", glyph_patch(lambda t, at, size: put16(t, at + 6, get16(t, at + 6) ^ 0x4000)))
    gv("gvar glyph first tuple embedded peak flag toggled", glyph_patch(lambda t, at, size: put16(t, at + 6, get16(t, at + 6) ^ 0x8000)))

    # cvar
    cv("cvar version 2", lambda t: put32(t, 0, 0x00020000))
    cv("cvar tuple count 4095", lambda t: put16(t, 4, (get16(t, 4) & 0xF000) | 0x0FFF))
    cv("cvar shared points flag", lambda t: put16(t, 4, get16(t, 4) | 0x8000))
    cv("cvar data offset past the table", lambda t: put16(t, 6, len(t) + 10))
    cv("cvar data offset 0", lambda t: put16(t, 6, 0))
    cv("cvar truncated", lambda t: bytearray(t[:len(t) - 20]))
    cv("cvar too short for the header", lambda t: bytearray(t[:6]))
    cv("cvar first tuple is a shared one", lambda t: put16(t, 10, get16(t, 10) & 0x7FFF))
    cv("cvar first tuple is shared, index past them", lambda t: put16(t, 10, (get16(t, 10) & 0x7000) | 0x0800))
    cv("cvar first tuple data size 0", lambda t: put16(t, 8, 0))
    cv("cvar first tuple private points toggled", lambda t: put16(t, 10, get16(t, 10) ^ 0x2000))
    cv("cvar first tuple intermediate toggled", lambda t: put16(t, 10, get16(t, 10) ^ 0x4000))
    out.append(("cvar missing", "cvar", None))

    # avar
    av("avar version 3", lambda t: put32(t, 0, 0x00030000))
    av("avar axis count 1", lambda t: put32(t, 4, 1))
    av("avar axis count 3", lambda t: put32(t, 4, 3))
    av("avar pair count 65535", lambda t: put16(t, 8, 0xFFFF))
    av("avar pair count 0", lambda t: put16(t, 8, 0))
    av("avar truncated", lambda t: bytearray(t[:len(t) - 12]))
    av("avar truncated to the header", lambda t: bytearray(t[:8]))
    av("avar a pair out of order", lambda t: put16(t, 10 + 4 * 2, 0xC000))
    out.append(("avar missing", "avar", None))

    # HVAR
    hv("HVAR version 2", lambda t: put16(t, 0, 2))
    hv("HVAR store offset past the table", lambda t: put32(t, 4, len(t) + 40))
    hv("HVAR store offset 0", lambda t: put32(t, 4, 0))
    hv("HVAR width map offset past the table", lambda t: put32(t, 8, len(t) + 40))
    hv("HVAR width map offset 0", lambda t: put32(t, 8, 0))
    hv("HVAR store format 2", lambda t: put16(t, 20, 2))
    hv("HVAR store data count 0", lambda t: put16(t, 26, 0))
    hv("HVAR store region axis count 3", lambda t: put16(t, 20 + get32(t, 22), 3))
    hv("HVAR store region count 40000", lambda t: put16(t, 20 + get32(t, 22) + 2, 40000))
    hv("HVAR store data subtable offset past the table", lambda t: put32(t, 28, len(t) + 100))
    hv("HVAR truncated", lambda t: bytearray(t[:len(t) - 30]))
    hv("HVAR a width map entry out of range", lambda t: t.__setitem__(len(t) - 1, 0xFF))
    out.append(("HVAR missing", "HVAR", None))

    # MVAR
    mv("MVAR version 2", lambda t: put16(t, 0, 2))
    mv("MVAR value count 5000", lambda t: put16(t, 8, 5000))
    mv("MVAR store offset past the table", lambda t: put16(t, 10, len(t) + 10))
    mv("MVAR store offset 0", lambda t: put16(t, 10, 0))
    mv("MVAR truncated", lambda t: bytearray(t[:len(t) - 40]))
    mv("MVAR a value indexes past the store", lambda t: put16(t, 12 + 8 * 2 + 4, 9))
    mv("MVAR two records of one tag", lambda t: t.__setitem__(slice(12 + 8 * 3, 12 + 8 * 3 + 4), b"gsp0"))
    out.append(("MVAR missing", "MVAR", None))

    # fvar
    fv("fvar axis size 24", lambda t: put16(t, 10, 24))
    fv("fvar zero axes", lambda t: put16(t, 8, 0))
    fv("fvar axis count 1", lambda t: put16(t, 8, 1))
    fv("fvar axis count 3", lambda t: put16(t, 8, 3))
    fv("fvar version 2", lambda t: put32(t, 0, 0x00020000))
    fv("fvar instance size 12", lambda t: put16(t, 14, 12))
    fv("fvar axes offset past the table", lambda t: put16(t, 4, len(t)))
    fv("fvar axis minimum above default", lambda t: t.__setitem__(slice(16 + 4, 16 + 8), struct.pack(">i", 500 << 16)))
    fv("fvar axis default above maximum", lambda t: t.__setitem__(slice(16 + 8, 16 + 12), struct.pack(">i", 900 << 16)))
    fv("fvar axis maximum equals default", lambda t: t.__setitem__(slice(16 + 12, 16 + 16), struct.pack(">i", 400 << 16)))
    out.append(("fvar missing", "fvar", None))

    return out


def flavor_variants_of(vertical, avar2):
    """Faults of the tables only the other two fonts have: VVAR and the vertical metrics of the vertical font, and the item variation store and axis map of the avar
    table of version 2 of the other. (name, the font they are made in, table, its new bytes or None)."""
    out = []

    def add(font_name, font, name, tag, fn):
        out.append((name, font_name, tag, edit(font, tag, fn)))

    def vv(name, fn):
        add("HintingVariableVertical.ttf", vertical, name, "VVAR", fn)

    vv("VVAR version 2", lambda t: put16(t, 0, 2))
    vv("VVAR store offset past the table", lambda t: put32(t, 4, len(t) + 40))
    vv("VVAR store offset 0", lambda t: put32(t, 4, 0))
    vv("VVAR height map offset past the table", lambda t: put32(t, 8, len(t) + 40))
    vv("VVAR height map offset 0", lambda t: put32(t, 8, 0))
    vv("VVAR store format 2", lambda t: put16(t, 24, 2))
    vv("VVAR store data count 0", lambda t: put16(t, 30, 0))
    vv("VVAR store region count 40000", lambda t: put16(t, 24 + get32(t, 26) + 2, 40000))
    vv("VVAR truncated", lambda t: bytearray(t[:len(t) - 30]))
    vv("VVAR map entry out of range", lambda t: t.__setitem__(len(t) - 1, 0xFF))
    out.append(("VVAR missing", "HintingVariableVertical.ttf", "VVAR", None))
    # (a vhea table with no long metrics, or a vmtx table that is cut short, is one the package's own font reader refuses, which is not what is compared here)
    add("HintingVariableVertical.ttf", vertical, "vhea truncated", "vhea", lambda t: bytearray(t[:30]))
    out.append(("vhea missing", "HintingVariableVertical.ttf", "vhea", None))
    out.append(("vmtx missing", "HintingVariableVertical.ttf", "vmtx", None))

    def a2(name, fn):
        add("HintingVariableAvar2.ttf", avar2, name, "avar", fn)

    # the version 2 table: 48 bytes of header and segment maps, then the offsets of the axis map (48) and of the store (52)
    a2("avar2 store offset past the table", lambda t: put32(t, 52, len(t) + 50))
    a2("avar2 store offset 0", lambda t: put32(t, 52, 0))
    a2("avar2 axis map offset past the table", lambda t: put32(t, 48, len(t) + 50))
    a2("avar2 axis map offset 0", lambda t: put32(t, 48, 0))
    a2("avar2 store format 2", lambda t: put16(t, get32(t, 52), 2))
    a2("avar2 store data count 0", lambda t: put16(t, get32(t, 52) + 6, 0))
    a2("avar2 store region axis count 3", lambda t: put16(t, get32(t, 52) + get32(t, get32(t, 52) + 2), 3))
    a2("avar2 store region count 40000", lambda t: put16(t, get32(t, 52) + get32(t, get32(t, 52) + 2) + 2, 40000))
    a2("avar2 axis map format 2", lambda t: t.__setitem__(get32(t, 48), 2))
    a2("avar2 axis map entry format bad", lambda t: t.__setitem__(get32(t, 48) + 1, 0xC0))
    a2("avar2 axis map count 65535", lambda t: put16(t, get32(t, 48) + 2, 0xFFFF))
    a2("avar2 axis map entry out of range", lambda t: t.__setitem__(get32(t, 48) + 4, 0xFC))
    a2("avar2 axis map entry unused", lambda t: t.__setitem__(slice(get32(t, 48) + 4, get32(t, 48) + 6), b"\xff\xff"))
    a2("avar2 truncated", lambda t: bytearray(t[:len(t) - 20]))
    a2("avar2 truncated to the segment maps", lambda t: bytearray(t[:48]))
    a2("avar2 truncated in the offsets", lambda t: bytearray(t[:52]))
    a2("avar2 version 3", lambda t: put32(t, 0, 0x00030000))
    return out


# the tables the mutants change in each font, with how many of them are made of the whole
MUTATED = [
    ("HintingVariable.ttf", 40, [("gvar", 10), ("cvar", 3), ("avar", 1), ("HVAR", 3), ("MVAR", 2), ("fvar", 1)]),
    ("HintingVariableNoHvar.ttf", 8, [("gvar", 10), ("cvar", 2), ("MVAR", 1)]),
    ("HintingVariableVertical.ttf", 26, [("VVAR", 6), ("HVAR", 2), ("gvar", 3), ("MVAR", 1)]),
    ("HintingVariableAvar2.ttf", 26, [("avar", 8), ("HVAR", 2), ("gvar", 2), ("fvar", 1)]),
]


def mutants_of(rng, fonts, count):
    """`count` fonts made by changing one to four bytes of one variation table of one of the four fonts (`fonts` maps a name to its bytes):
    (the font, table, [(offset in the table, value)], the new table)."""
    names = [n for n, _, _ in MUTATED]
    out = []
    for _ in range(count):
        name = rng.choices(names, [w for _, w, _ in MUTATED])[0]
        tables = dict((n, t) for n, _, t in MUTATED)[name]
        tag = rng.choices([t for t, _ in tables], [w for _, w in tables])[0]
        table = bytearray(table_bytes(fonts[name], tag))
        edits = []
        for _ in range(rng.choice([1, 1, 2, 3, 4])):
            # the header of a table decides the most, so the first bytes are changed more often
            at = rng.randrange(min(len(table), 48)) if rng.random() < 0.45 else rng.randrange(len(table))
            value = rng.choice([0, 0xFF, 0x80, 0x01, 0x7F, (table[at] + 1) & 0xFF, (table[at] - 1) & 0xFF, rng.randrange(256)])
            table[at] = value
            edits.append((at, value))
        out.append((name, tag, edits, bytes(table)))
    return out


def record_blob(freetype, raw, data, glyphs, locations, sizes=(16 * 64,), with_gasp=True):
    """What FreeType makes of a font that may be broken: whether it opens the face and, for each location, whether it can be set and a digest of every glyph."""
    lib, face, cbuf, error = open_face(freetype, raw, data, 40)
    if error:
        raw.FT_Done_FreeType(lib)
        return {"error": error}
    close_face(raw, lib, face)

    axes = axis_count(raw, freetype, data)
    runs = []
    for location in locations:
        lib, face, cbuf, error = open_face(freetype, raw, data, 40)
        err, ndv = set_location(raw, face, location, axes)
        run = {"set": err, "ndv": ndv}
        if not err:
            if with_gasp:
                run["gasp"] = gasp_flags(raw, face, range(0, 41))
            flags = FT_LOAD_NO_BITMAP | FT_LOAD_NO_AUTOHINT
            per = {}
            for size in sizes:
                for gid in glyphs:
                    # the size is requested again for each glyph so that FreeType runs the CVT program afresh (see generate_hinting_golden.py)
                    assert raw.FT_Set_Char_Size(face, size, size, 72, 72) == 0
                    per[str(gid)] = digest(glyph_record(raw, face, gid, flags))
            run["glyphs"] = per
        runs.append(run)
        close_face(raw, lib, face)
    return {"error": 0, "axes": axes, "runs": runs}


def main():
    import base64

    parser = argparse.ArgumentParser()
    parser.add_argument("--freetype", help="path to a FreeType shared library built from the tag the port derives from")
    parser.add_argument("--allow-other-version", action="store_true")
    parser.add_argument("--out", default=OUT_GOLDEN)
    parser.add_argument("--variants-out", default=OUT_VARIANTS)
    parser.add_argument("--mutants", type=int, default=600, help="how many mutants to record (the committed reference has 600)")
    parser.add_argument("--mutant-seed", type=int, default=1)
    parser.add_argument("--only-variants", action="store_true", help="record only the variants and mutants (with --variants-out somewhere else, for a bigger fuzz)")
    args = parser.parse_args()

    freetype, raw = load_freetype(args.freetype)
    version = freetype.version()
    if version != (2, 14, 3) and not args.allow_other_version:
        sys.exit("FreeType %d.%d.%d loaded; the port derives from 2.14.3 (see --freetype)" % version)

    os.environ.setdefault("SOURCE_DATE_EPOCH", "1767225600")
    font, menu, glyph_count, rng = build_base_font()
    buf = io.BytesIO()
    font.save(buf)
    base_bytes = buf.getvalue()

    fonts = flavors(base_bytes, menu, glyph_count, rng)

    if not args.only_variants:
        result = {
            "freetype": {"version": "%d.%d.%d" % version, "tag": "VER-2-14-3"},
            "format": "per glyph: a=advance (26.6), e=contour end points, x/y=coordinates (26.6), t=1 for an on-curve point, err=FreeType's error; per location: the "
                      "design coordinates or the raw normalized ones FreeType was given, set=FreeType's error from setting them, ndv=the normalized vector it kept "
                      "(16.16, one for each axis), gasp=FT_Get_Gasp for the ppems 0 to 40",
            "fonts": [],
        }

        all_glyphs = list(range(glyph_count))
        for file_name, data in fonts:
            with open(os.path.join(HERE, file_name), "wb") as f:
                f.write(data)
            print("wrote", file_name, len(data), "bytes")
            # the main font is recorded at every glyph; the flavors (which differ in the tables that touch the metrics and the normalization) at fewer
            glyphs = all_glyphs if file_name == "HintingVariable.ttf" else all_glyphs[::3]
            entry = record_font(freetype, raw, file_name, data, glyphs, with_gasp=True)
            report(entry)
            result["fonts"].append(entry)

        for file_name in BUNDLED:
            with open(os.path.join(HERE, file_name), "rb") as f:
                data = f.read()
            from fontTools.ttLib import TTFont
            n = len(TTFont(io.BytesIO(data)).getGlyphOrder())
            entry = record_font(freetype, raw, file_name, data, list(range(n)), with_gasp=False)
            report(entry)
            result["fonts"].append(entry)

        with gzip.GzipFile(args.out, "wb", mtime=0) as f:
            f.write(json.dumps(result, separators=(",", ":")).encode("utf-8"))
        print("wrote", args.out, os.path.getsize(args.out), "bytes")

    # the fonts that are wrong
    main_font = fonts[0][1]
    ranges = axis_ranges(main_font)
    locations = [
        {"design": None, "blend": None},
        {"design": [design_value(ranges[0], 0), design_value(ranges[1], 0)], "blend": None},    # set to the defaults: a face that has a blend that varies nothing
        {"design": [design_value(ranges[0], 0.7), design_value(ranges[1], -0.3)], "blend": None},
        {"design": None, "blend": [19661, -45875]},
    ]
    probe = list(range(0, glyph_count, 4))
    small = {
        "freetype": {"version": "%d.%d.%d" % version, "tag": "VER-2-14-3"},
        "format": "a variant is the table of the font named (base) replaced by the base64 data (or left out: null); error is FreeType's error from opening the face (0: it opens); a run is a location (none: as it opens): set is FreeType's error "
                  "from setting it, ndv the normalized vector it kept, gasp FT_Get_Gasp for the ppems 0 to 40, glyphs a digest of every glyph (FNV-1a 64 of the advance, "
                  "the contour ends and the points; e<n> for FreeType's error n); a mutant is the font named (base) with the bytes of one of its tables changed at the "
                  "offsets given (offset from the start of the table, new value)",
        "glyphs": probe,
        "locations": [{"design": l["design"], "blend": l["blend"]} for l in locations],
        "variants": [],
        "mutants": [],
    }

    refused = 0
    base_variant = None
    by_name = dict(fonts)
    variants = [(name, "HintingVariable.ttf", tag, table) for name, tag, table in variants_of(main_font)]
    variants += flavor_variants_of(by_name["HintingVariableVertical.ttf"], by_name["HintingVariableAvar2.ttf"])
    for name, base_name, tag, table in variants:
        data = main_font if name == "baseline" else with_table(by_name[base_name], tag, table)
        entry = {"name": name, "base": base_name, "table": tag, "data": None if table is None else base64.b64encode(table).decode("ascii")}
        entry.update(record_blob(freetype, raw, data, probe, locations))
        small["variants"].append(entry)
        refused += sum(1 for r in entry.get("runs", []) if r["set"])
    print("  %d variants, %d locations that FreeType cannot set" % (len(small["variants"]), refused))

    mrng = random.Random(SEED + args.mutant_seed)
    unset = 0
    mutated = mutants_of(mrng, by_name, args.mutants)
    for base_name, tag, edits, table in mutated:
        data = with_table(by_name[base_name], tag, table)
        entry = {"base": base_name, "table": tag, "edits": [[o, v] for o, v in edits]}
        entry.update(record_blob(freetype, raw, data, probe, locations))
        small["mutants"].append(entry)
        unset += sum(1 for r in entry.get("runs", []) if r["set"])
    print("  %d mutants, %d locations that FreeType cannot set" % (len(small["mutants"]), unset))

    with gzip.GzipFile(args.variants_out, "wb", mtime=0) as f:
        f.write(json.dumps(small, separators=(",", ":")).encode("utf-8"))
    print("wrote", args.variants_out, os.path.getsize(args.variants_out), "bytes")


if __name__ == "__main__":
    main()
