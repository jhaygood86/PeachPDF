#!/usr/bin/env python3
"""Generates the CFF2 fixtures of the hinting tests (HintingCff2*.otf) and their references, HintingCff2.golden.json.gz and
HintingCff2Variants.golden.json.gz: what FreeType itself makes of every glyph of them, for the CFF2 branches of the port of Adobe's CFF
engine (PeachDrawing.Text/Internal/Hinting/FreeType) to be compared with exactly, in 26.6 units.

A CFF2 font is the variable form of CFF: its charstrings have no width and no endchar, and two more operators, vsindex and blend, by which
the operands of a glyph (its hints and its path) are written as a default and a delta for each region of the design space; its Private
DICTs can blend as well (the blue zones and the stem widths of a design move with the weight). FreeType computes every factor in 16.16
fixed point (cffload.c: cff_blend_build_vector), so the port has to as well for the outlines to be the same.

The fixtures (see hinting_cff2_builder.py for how they are written, byte by byte):

  HintingCff2.otf         three Font DICTs picked by an FDSelect, each with a Private DICT of its own (blended blue zones and stem widths,
                          another vsindex, another LanguageGroup) and local subroutines, two axes with an avar table, four data sets over
                          six regions; every glyph is a random Type 2 charstring whose operands are partly blended (hints included)
  HintingCff2Single.otf   one Font DICT and no FDSelect, no vsindex in the Private DICT (data set 0), local subroutines that blend
  HintingCff2NoAxes.otf   a CFF2 font that is not variable at all (no fvar): FreeType's normalized vector is empty and every blend collapses
                          to its default
  VariableCff2Test.otf    the bundled real variable CFF2 font (its glyphs have no hints; its HVAR moves the advances)

Each is recorded at several locations of its design space: at design coordinates FreeType itself normalizes (with the avar table) and at
raw normalized coordinates that are not multiples of 1/16384, which the API cannot reach but the engine can be given. Each location is
recorded with the normalized vector FreeType kept (FT_Get_Var_Blend_Coordinates), which the tests hand to the port.

A second file holds small fonts made by hand whose tables are wrong in one deliberate way (each with whether FreeType opens the face and
what it makes of the glyphs), and mutants: a fixture with a few bytes of its CFF2 table changed, so that whatever a damaged table does to
FreeType (refuse the face, refuse a glyph, draw it differently) the port has to do too.

Usage (see generate_hinting_golden.py for --freetype):

  python generate_hinting_cff2_fixtures.py --freetype path/to/freetype.dll

Requires freetype-py and fontTools.
"""
import argparse
import base64
import ctypes
import gzip
import importlib.util
import io
import json
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import hinting_cff2_builder as b  # noqa: E402
import hinting_cff2_variants as variants  # noqa: E402

# the generator of the CFF fixtures: its random Type 2 charstrings and the operator numbers
_spec = importlib.util.spec_from_file_location("hinting_cff1", os.path.join(HERE, "generate_hinting_cff_fixtures.py"))
cff1 = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(cff1)
num, op = cff1.num, cff1.op

OUT_GOLDEN = os.path.join(HERE, "HintingCff2.golden.json.gz")
OUT_VARIANTS = os.path.join(HERE, "HintingCff2Variants.golden.json.gz")

SEED = 0xCFF20

FT_LOAD_NO_BITMAP = 0x8
FT_LOAD_NO_AUTOHINT = 0x8000

# 26.6 sizes recorded
SIZES = [9 * 64, 16 * 64, 30 * 64]

VSINDEX, BLEND = 15, 16

# the operators that take their operands from the stack and clear it (an operand run in front of one can be blended)
CLEARING = {1, 3, 4, 5, 6, 7, 8, 14, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 30, 31}


# ---- charstrings ---------------------------------------------------------------------------------------------------------------

def tokenize(data):
    """Splits a charstring into numbers ('n', bytes) and operators ('o', bytes); the bytes of a hint mask belong to its operator."""
    tokens = []
    i = 0
    stems = 0
    pending = 0
    while i < len(data):
        c = data[i]
        if c == 28:
            tokens.append(("n", data[i:i + 3]))
            i += 3
            pending += 1
        elif c >= 32:
            n = 5 if c == 255 else 2 if c >= 247 else 1
            tokens.append(("n", data[i:i + n]))
            i += n
            pending += 1
        else:
            n = 2 if c == 12 else 1
            code = data[i:i + n]
            i += n
            if c in (1, 3, 18, 23):
                stems += pending // 2
            if c in (19, 20):
                stems += pending // 2
                nbytes = (stems + 7) // 8
                code += data[i:i + nbytes]
                i += nbytes
            tokens.append(("o", code))
            pending = 0
    return tokens


def random_delta(rng):
    """A delta of a blend: mostly whole numbers, sometimes a number that is not."""
    if rng.random() < 0.15:
        return num(rng.randint(-60, 60) + rng.choice([0.25, 0.5, 0.75]))
    return num(rng.randint(-60, 60))


def inject_blends(rng, data, regions, probability):
    """Rewrites some of the operand runs of a charstring as blends over a data set of `regions` regions: the values as they were (the
    defaults), then a delta for each region of each value, the number of values, and the operator."""
    if regions == 0:
        return data
    out = []
    numbers = 0
    for token in tokenize(data):
        if token[0] == "n":
            out.append(token[1])
            numbers += 1
            continue

        code = token[1]
        clearing = code[1] in (34, 35, 36, 37) if code[0] == 12 else code[0] in CLEARING
        if clearing and numbers and rng.random() < probability:
            m = numbers if rng.random() < 0.5 else rng.randint(1, numbers)
            deltas = b"".join(random_delta(rng) for _ in range(m * regions))
            out.append(deltas + num(m) + bytes([BLEND]))
        out.append(code)
        numbers = 0
    return b"".join(out)


class Glyph2(cff1.Glyph):
    """A random Type 2 charstring of a CFF2 font: no width."""

    def __init__(self, rng, font):
        super().__init__(rng, font)
        self.active_ds = 0

    def width_arg(self):
        return []

    def arithmetic(self):
        # The arithmetic, storage and conditional operators are not CFF2's: an interpreter ignores one and clears the stack. The operator
        # of a fixture is followed by a whole line, so that the glyph is still one that can be drawn.
        r = self.rng
        code = r.choice([cff1.AND, cff1.OR, cff1.NOT, cff1.ABS, cff1.ADD, cff1.SUB, cff1.DIV, cff1.NEG, cff1.EQ, cff1.DROP, cff1.PUT, cff1.GET,
                         cff1.IFELSE, cff1.RANDOM, cff1.MUL, cff1.SQRT, cff1.DUP, cff1.EXCH, cff1.INDEX, cff1.ROLL])
        self.emit(*[num(r.randint(-90, 90)) for _ in range(r.randint(0, 3))], op(code, True))
        self.emit(num(r.randint(-80, 80)), num(r.randint(-80, 80)), op(cff1.RLINETO))

    def subr_call(self):
        r = self.rng
        font = self.font
        if font["local_subrs"] and r.random() < 0.6:
            n = len(font["local_subrs"])
            i = r.randrange(n)
            ds = font["blend_subrs"].get(i)
            # a subroutine that blends over one data set is only called under it (almost always: the rest make garbage on purpose)
            if ds is not None and ds != self.active_ds and r.random() < 0.9:
                return
            self.emit(num(i - cff1.bias(n)), op(cff1.CALLSUBR))
        elif font["global_subrs"]:
            n = len(font["global_subrs"])
            self.emit(num(r.randrange(n) - cff1.bias(n)), op(cff1.CALLGSUBR))


def make_glyph(rng, font, private_ds, region_counts, blend_probability=0.4):
    """One glyph: sometimes a vsindex of its own first, its operands partly blended, sometimes an endchar (which CFF2 ignores)."""
    explicit = rng.random() < 0.35
    ds = rng.randrange(len(region_counts)) if explicit else private_ds
    g = Glyph2(rng, font)
    g.active_ds = ds
    data = g.make()
    data = inject_blends(rng, data, region_counts[ds], blend_probability)
    if explicit:
        data = num(ds) + bytes([VSINDEX]) + data
    if rng.random() < 0.5 and data.endswith(bytes([14])):
        data = data[:-1]
    return data


def make_subr(rng, font, kind, ds, region_counts, blend):
    g = cff1.Glyph(rng, font)
    g.have_width = True
    if kind == "hints":
        args, count = g.hints(False)
        if count:
            g.emit(args, op(cff1.HSTEMHM))
    else:
        g.path(rng.randint(1, 4))
    data = bytes(g.out) + op(cff1.RETURN)
    return inject_blends(rng, data, region_counts[ds], 0.6) if blend else data


# ---- the fonts -----------------------------------------------------------------------------------------------------------------

# the design space of the fixtures (hinting_cff2_builder.py)
AXES, AVAR, REGIONS, DATA_SETS = b.AXES, b.AVAR, b.REGIONS, b.DATA_SETS
REGION_COUNTS = [len(d) for d in DATA_SETS]


def blended_blues(values, rng):
    """BlueValues with the top edges blended over a data set of two regions (the deltas move the zones with the weight)."""
    out = []
    for i, v in enumerate(values):
        out.append(b.blended(v, [rng.randint(-6, 6), rng.randint(-6, 6)]) if i >= 2 and rng.random() < 0.6 else v)
    return out


def latin_private(rng, regions, blends, language_group=0, blue_edges=None, vsindex=None, std=(68, 84), blue_values=None):
    """The entries of a Private DICT: blue zones, stem widths and the rest of what the engine reads; `regions` is the number of regions of
    the data set of the DICT's vsindex, for the blends."""
    def blend_of(base, scale=1):
        return b.blended(base, [rng.randint(-8, 8) * scale for _ in range(regions)]) if blends and regions else base

    blues = blue_values or [-15, 0, 486, 500, 700, 715]
    entries = []
    if vsindex is not None:
        entries.append((b.VSINDEX, [vsindex]))
    if language_group:
        entries.append((b.LANGUAGE_GROUP, [language_group]))
    if blends:
        values = [v if i < 2 or rng.random() < 0.4 else blend_of(v) for i, v in enumerate(blues)]
    else:
        values = list(blues)
    entries.append((b.BLUE_VALUES, b.delta_values(values)))
    entries.append((b.OTHER_BLUES, b.delta_values([-235, -220])))
    entries.append((b.FAMILY_BLUES, b.delta_values([-14, 0, 487, 500])))
    entries.append((b.STD_HW, [blend_of(std[0])]))
    entries.append((b.STD_VW, [blend_of(std[1])]))
    entries.append((b.BLUE_SCALE, [0.039625], b.dreal))
    entries.append((b.BLUE_SHIFT, [7]))
    entries.append((b.BLUE_FUZZ, [1]))
    return entries


def build_multi_font(rng):
    """Three Font DICTs picked by an FDSelect of format 3, each with a Private DICT of its own; 90 glyphs."""
    n_glyphs = 90
    names = [".notdef"] + ["g%d" % i for i in range(1, n_glyphs)]
    select = [0 if i < 30 else 1 if i < 60 else 2 for i in range(n_glyphs)]
    # (vsindex of the Private DICT or None, LanguageGroup, blue zones, stem widths)
    specs = [
        (None, 0, [-15, 0, 486, 500, 700, 715], (68, 84)),
        (1, 0, [-10, 0, 470, 480, 690, 700], (50, 90)),
        (2, 1, [-250, 0, 1100, 1120], (40, 100)),
    ]
    global_subrs = []
    fds = []
    contexts = []
    for k, (vsindex, language_group, blues, std) in enumerate(specs):
        ds = vsindex or 0
        edges = blues[:] + [-235, -220]
        ctx = {"blue_edges": edges, "local_subrs": [], "global_subrs": [], "blend_subrs": {}}
        subrs = []
        for i in range(10):
            blend = i in (3, 7)
            subrs.append(make_subr(rng, ctx, "hints" if i % 5 == 0 else "path", ds, REGION_COUNTS, blend))
            if blend:
                ctx["blend_subrs"][i] = ds
        ctx["local_subrs"] = subrs
        contexts.append(ctx)
        entries = latin_private(rng, REGION_COUNTS[ds], True, language_group, edges, vsindex, std, blues)
        fds.append(b.FontDict(b.private_dict(entries, True), subrs))
    for i in range(6):
        global_subrs.append(make_subr(rng, contexts[0], "hints" if i % 6 == 0 else "path", 0, REGION_COUNTS, False))
    for ctx in contexts:
        ctx["global_subrs"] = global_subrs

    charstrings = []
    for i in range(n_glyphs):
        k = select[i]
        ds = specs[k][0] or 0
        if i == 0:
            charstrings.append(num(500) + op(cff1.HMOVETO))
        else:
            charstrings.append(make_glyph(rng, contexts[k], ds, REGION_COUNTS))

    table = b.build_table(charstrings, global_subrs, fds, b.fd_select(3, select), b.variation_store(2, REGIONS, DATA_SETS))
    return b.build_font(table, names, AXES, AVAR), n_glyphs


def build_single_font(rng):
    """One Font DICT, no FDSelect, no vsindex in its Private DICT: data set 0; local subroutines that blend; 40 glyphs."""
    n_glyphs = 40
    names = [".notdef"] + ["g%d" % i for i in range(1, n_glyphs)]
    blues = [-15, 0, 486, 500, 700, 715]
    ctx = {"blue_edges": blues + [-235, -220], "local_subrs": [], "global_subrs": [], "blend_subrs": {}}
    subrs = []
    for i in range(8):
        blend = i % 3 == 1
        subrs.append(make_subr(rng, ctx, "hints" if i % 4 == 0 else "path", 0, REGION_COUNTS, blend))
        if blend:
            ctx["blend_subrs"][i] = 0
    ctx["local_subrs"] = subrs
    entries = latin_private(rng, REGION_COUNTS[0], True, 0, ctx["blue_edges"], None, (68, 84), blues)
    charstrings = [num(500) + op(cff1.HMOVETO)] + [make_glyph(rng, ctx, 0, REGION_COUNTS) for _ in range(1, n_glyphs)]
    table = b.build_table(charstrings, [], [b.FontDict(b.private_dict(entries, True), subrs)], None, b.variation_store(2, REGIONS, DATA_SETS))
    return b.build_font(table, names, AXES, AVAR), n_glyphs


def build_noaxes_font(rng):
    """A CFF2 font that is not variable (no fvar) but has a variation store and blends: FreeType has no normalized vector for it."""
    n_glyphs = 30
    names = [".notdef"] + ["g%d" % i for i in range(1, n_glyphs)]
    blues = [-15, 0, 486, 500, 700, 715]
    ctx = {"blue_edges": blues + [-235, -220], "local_subrs": [], "global_subrs": [], "blend_subrs": {}}
    entries = latin_private(rng, REGION_COUNTS[0], True, 0, ctx["blue_edges"], None, (68, 84), blues)
    charstrings = [num(500) + op(cff1.HMOVETO)] + [make_glyph(rng, ctx, 0, REGION_COUNTS) for _ in range(1, n_glyphs)]
    table = b.build_table(charstrings, [], [b.FontDict(b.private_dict(entries, False))], None, b.variation_store(2, REGIONS, DATA_SETS))
    return b.build_font(table, names, None), n_glyphs


# ---- FreeType ------------------------------------------------------------------------------------------------------------------

def load_freetype(path):
    return cff1.load_freetype(path)


def fixed_array(values):
    return (ctypes.c_long * len(values))(*values)


def set_location(raw, face, location, axes):
    """Puts the face at a location: design coordinates (which FreeType normalizes, with the avar table) or raw normalized ones. Returns the
    normalized vector FreeType kept, or None when the location says nothing (a font with no axes, or one left as it opens)."""
    if axes == 0:
        return None
    if location.get("design") is not None:
        values = [int(round(v * 65536)) for v in location["design"]]
        assert raw.FT_Set_Var_Design_Coordinates(face, len(values), fixed_array(values)) == 0
    elif location.get("blend") is not None:
        assert raw.FT_Set_Var_Blend_Coordinates(face, axes, fixed_array(location["blend"])) == 0
    out = fixed_array([0] * axes)
    assert raw.FT_Get_Var_Blend_Coordinates(face, axes, out) == 0
    return [int(v) for v in out]


def glyph_record(raw, face, gid):
    error = raw.FT_Load_Glyph(face, gid, FT_LOAD_NO_BITMAP | FT_LOAD_NO_AUTOHINT)
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
        "t": [outline.tags[i] & 3 for i in range(n)],
    }


def open_face(freetype, raw, data, darken=False):
    cbuf = ctypes.create_string_buffer(data, len(data))
    lib = freetype.FT_Library()
    assert raw.FT_Init_FreeType(ctypes.byref(lib)) == 0
    dark = ctypes.c_int(0 if darken else 1)
    assert raw._lib.FT_Property_Set(lib, b"cff", b"no-stem-darkening", ctypes.byref(dark)) == 0
    engine = ctypes.c_uint(1)  # FT_HINTING_ADOBE
    assert raw._lib.FT_Property_Set(lib, b"cff", b"hinting-engine", ctypes.byref(engine)) == 0
    face = freetype.FT_Face()
    error = raw.FT_New_Memory_Face(lib, cbuf, len(data), 0, ctypes.byref(face))
    return lib, face, cbuf, error


def axis_ranges(data):
    """The (minimum, default, maximum) of every axis of a font, from its fvar table."""
    from fontTools.ttLib import TTFont
    return [(a.minValue, a.defaultValue, a.maxValue) for a in TTFont(io.BytesIO(data))["fvar"].axes]


def axis_count(raw, freetype, data):
    """The number of axes FreeType sees in the font (0 when it is not variable)."""
    lib, face, cbuf, error = open_face(freetype, raw, data)
    assert error == 0
    count = 0
    if face.contents.face_flags & (1 << 8):  # FT_FACE_FLAG_MULTIPLE_MASTERS
        var = ctypes.c_void_p()
        assert raw.FT_Get_MM_Var(face, ctypes.byref(var)) == 0
        count = ctypes.cast(var, ctypes.POINTER(ctypes.c_uint)).contents.value
    raw.FT_Done_Face(face)
    raw.FT_Done_FreeType(lib)
    return count


def record_run(freetype, raw, data, glyphs, location, size, darken, axes, fresh_per_glyph=False):
    """What FreeType makes of the glyphs at a location and a size: one face for the whole run, as an application would use it (or a face
    for each glyph, to check that a glyph does not depend on the ones loaded before it)."""
    result = {}
    lib = face = cbuf = None
    for gid in glyphs:
        if face is None or fresh_per_glyph:
            if face is not None:
                raw.FT_Done_Face(face)
                raw.FT_Done_FreeType(lib)
            lib, face, cbuf, error = open_face(freetype, raw, data, darken)
            assert error == 0
            set_location(raw, face, location, axes)
            assert raw.FT_Set_Char_Size(face, size, size, 72, 72) == 0
        result[str(gid)] = glyph_record(raw, face, gid)
    raw.FT_Done_Face(face)
    raw.FT_Done_FreeType(lib)
    return result


# where the recorded locations are: a fraction of each axis's range before its avar table (positive: from the default towards the
# maximum, negative: towards the minimum). They are dyadic, so that the design coordinates FreeType normalizes in 16.16 and the package in
# 2.14 come to the same number, which the API tests check.
FRACTIONS = [
    ("default", (0, 0)),
    ("not set", None),
    ("first axis 1", (1, 0)),
    ("first axis -1", (-1, 0)),
    ("first axis 0.5", (0.5, 0)),
    ("first axis -0.5", (-0.5, 0)),
    ("second axis 1", (0, 1)),
    ("second axis -1", (0, -1)),
    ("both 0.5", (0.5, 0.5)),
    ("first axis 0.25", (0.25, 0)),
    ("first axis -0.125", (-0.125, 0)),
    ("0.75 and -0.5", (0.75, -0.5)),
]
# normalized coordinates that are not multiples of 1/16384 (in 16.16: 0.3, -0.7 and so on)
RAW = [("raw 0.3 -0.7", [19661, -45875]), ("raw 0.81 0.33", [53084, 21627]), ("raw -0.45 0.12", [-29491, 7864])]


def design_value(axis, fraction):
    minimum, default, maximum = axis
    return default + fraction * (maximum - default if fraction > 0 else default - minimum)


def locations_for(axes, axis_ranges=None):
    """The locations recorded for a font with the axes `axis_ranges` (a list of (minimum, default, maximum)), or none."""
    if axes == 0:
        return [{"name": "default", "design": None, "blend": None}]
    out = []
    for name, fractions in FRACTIONS:
        design = None if fractions is None else [design_value(axis_ranges[i], fractions[i] if i < 2 else 0) for i in range(axes)]
        out.append({"name": name, "design": design, "blend": None})
    for name, blend in RAW:
        out.append({"name": name, "design": None, "blend": (blend + [0] * axes)[:axes]})
    return out


def record_font(freetype, raw, file_name, data, n_glyphs, sizes=SIZES, darkened_locations=(0, 4, 12), check_fresh=True):
    axes = axis_count(raw, freetype, data)
    ranges = axis_ranges(data) if axes else None
    locations = locations_for(axes, ranges)
    glyphs = list(range(n_glyphs))
    entry = {"file": file_name, "axes": axes, "locations": [], "modes": {"standard": [], "darkened": []}}

    for li, location in enumerate(locations):
        lib, face, cbuf, error = open_face(freetype, raw, data)
        assert error == 0
        entry["locations"].append({"name": location["name"], "design": location["design"], "ndv": set_location(raw, face, location, axes)})
        raw.FT_Done_Face(face)
        raw.FT_Done_FreeType(lib)

    for li, location in enumerate(locations):
        for size in sizes:
            entry["modes"]["standard"].append({"size": size, "loc": li, "glyphs": record_run(freetype, raw, data, glyphs, location, size, False, axes)})
        if li in darkened_locations:
            for size in (sizes[0], sizes[1]):
                entry["modes"]["darkened"].append({"size": size, "loc": li, "glyphs": record_run(freetype, raw, data, glyphs, location, size, True, axes)})

    if check_fresh and axes:
        # a glyph must not depend on the glyphs loaded before it (the port loads every glyph from a fresh state)
        for li in (4, 12):
            same = record_run(freetype, raw, data, glyphs, locations[li], sizes[1], False, axes)
            fresh = record_run(freetype, raw, data, glyphs, locations[li], sizes[1], False, axes, fresh_per_glyph=True)
            assert same == fresh, "%s: FreeType's outlines depend on the order glyphs are loaded in" % file_name
    if axes:
        # a face left as it opens is at the default location
        standard = {(r["loc"], r["size"]): r["glyphs"] for r in entry["modes"]["standard"]}
        for size in sizes:
            assert standard[(0, size)] == standard[(1, size)], "a face that is not set differs from one set to the default"
    return entry


# the locations a small font is recorded at: as it opens, at the top of the first axis, and at a location that is not a multiple of 1/16384
BLOB_LOCATIONS = [None, [65536, 0], [19661, -45875]]


def record_blob(freetype, raw, data, glyph_count, sizes=(16 * 64,), locations=BLOB_LOCATIONS):
    """What FreeType makes of a small font that may be broken: whether it opens the face and, if it does, of every glyph at a few
    locations (the ones that need an axis are skipped for a font without one)."""
    lib, face, cbuf, error = open_face(freetype, raw, data)
    if error:
        raw.FT_Done_FreeType(lib)
        return {"error": error}
    raw.FT_Done_Face(face)
    raw.FT_Done_FreeType(lib)

    axes = axis_count(raw, freetype, data)
    runs = []
    for blend in locations:
        if blend is not None and not axes:
            continue
        for size in sizes if blend is None else sizes[:1]:
            location = {"design": None, "blend": None if blend is None else (blend + [0] * axes)[:axes]}
            lib, face, cbuf, error = open_face(freetype, raw, data)
            ndv = set_location(raw, face, location, axes)
            assert raw.FT_Set_Char_Size(face, size, size, 72, 72) == 0
            glyphs = {str(g): glyph_record(raw, face, g) for g in range(glyph_count)}
            raw.FT_Done_Face(face)
            raw.FT_Done_FreeType(lib)
            runs.append({"ndv": ndv, "size": size, "glyphs": glyphs})
    return {"error": 0, "axes": axes, "runs": runs}


def record_variants(freetype, raw, rng, mutant_count=None, mutant_base="baseline"):
    """The variants, and the mutants of the baseline, with what FreeType makes of them."""
    result = {
        "freetype": {"version": "%d.%d.%d" % freetype.version(), "tag": "VER-2-14-3"},
        "format": "a font is base64; error is FreeType's error from opening the face (0: it opens); runs are the glyph records at a location "
                  "(ndv, or null when the font is left as it opens) and size; a mutant is the baseline with the bytes of its CFF2 table changed "
                  "at the offsets given (offset from the start of the table, new value)",
        "cases": [],
        "variants": [],
        "mutants": [],
    }

    font, count, names = variants.charstring_cases()
    entry = {"name": "charstring cases", "font": base64.b64encode(font).decode("ascii"), "names": names}
    entry.update(record_blob(freetype, raw, font, count, sizes=(16 * 64, 9 * 64)))
    result["cases"].append(entry)
    ok = sum(1 for r in entry["runs"] for g in r["glyphs"].values() if "err" not in g)
    print("  charstring cases: %d glyphs, %d loads at the three locations" % (count, ok))

    refused = 0
    base_font = None
    for name, data, glyph_count, _axes in variants.table_variants():
        entry = {"name": name, "font": base64.b64encode(data).decode("ascii")}
        entry.update(record_blob(freetype, raw, data, glyph_count))
        result["variants"].append(entry)
        refused += 1 if entry["error"] else 0
        if name == "baseline":
            base_font = data
    print("  %d variants, %d of them refused" % (len(result["variants"]), refused))

    if mutant_base == "baseline":
        table, mutant_edits = variants.mutants(rng, MUTANTS if mutant_count is None else mutant_count)
        assert bytes(b.table_of(base_font, "CFF2")) == table, "the baseline of the mutants is not the baseline variant"
        glyphs = 8
    elif mutant_base == "cases":
        # the font of the charstring cases, for a bigger fuzz of the charstrings
        base_font, glyphs, _ = variants.charstring_cases()
        table, mutant_edits = variants.mutants(rng, mutant_count, bytes(b.table_of(base_font, "CFF2")))
    else:
        # one of the random fixtures
        with open(os.path.join(HERE, mutant_base), "rb") as f:
            base_font = f.read()
        glyphs = 40
        table, mutant_edits = variants.mutants(rng, mutant_count, bytes(b.table_of(base_font, "CFF2")))
        result["cases"].append({"name": mutant_base, "font": base64.b64encode(base_font).decode("ascii")})
    opened = 0
    # the bytes are changed in the font itself, not in a font written again around the new table: a damaged INDEX can read on into the tables
    # that follow (and their checksums are among the bytes it reads), so the port has to be given the very bytes FreeType is
    table_offset = b.table_offset(base_font, "CFF2")
    for edits in mutant_edits:
        edited = bytearray(base_font)
        for offset, value in edits:
            edited[table_offset + offset] = value
        data = bytes(edited)
        entry = {"base": {"baseline": "baseline", "cases": "charstring cases"}.get(mutant_base, mutant_base), "edits": [[o, v] for o, v in edits]}
        entry.update(record_blob(freetype, raw, data, glyphs, locations=[None, [19661, -45875]]))
        result["mutants"].append(entry)
        opened += 0 if entry["error"] else 1
    print("  %d mutants, %d of them opened" % (len(result["mutants"]), opened))
    return result


MUTANTS = 400


def report(entry):
    loaded = refused = 0
    for runs in entry["modes"].values():
        for run in runs:
            for g in run["glyphs"].values():
                if "err" in g:
                    refused += 1
                else:
                    loaded += 1
    print("  %s: %d loaded, %d refused" % (entry["file"], loaded, refused))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--freetype", help="path to a FreeType shared library built from the tag the port derives from")
    parser.add_argument("--allow-other-version", action="store_true")
    parser.add_argument("--out", default=OUT_GOLDEN)
    parser.add_argument("--variants-out", default=OUT_VARIANTS)
    parser.add_argument("--mutants", type=int, default=MUTANTS, help="how many mutants of the baseline to record (the committed reference has 400)")
    parser.add_argument("--only-variants", action="store_true", help="record only the variants and mutants (with --variants-out somewhere else, for a bigger fuzz)")
    parser.add_argument("--mutant-seed", type=int, default=1)
    parser.add_argument("--mutant-base", default="baseline", help="baseline, cases (the charstring cases), or the file name of a fixture")
    args = parser.parse_args()

    freetype, raw = load_freetype(args.freetype)
    version = freetype.version()
    if version != (2, 14, 3) and not args.allow_other_version:
        sys.exit("FreeType %d.%d.%d loaded; the port derives from 2.14.3 (see --freetype)" % version)

    if args.only_variants:
        small = record_variants(freetype, raw, random.Random(SEED + args.mutant_seed), args.mutants, args.mutant_base)
        with gzip.GzipFile(args.variants_out, "wb", mtime=0) as f:
            f.write(json.dumps(small, separators=(",", ":")).encode("utf-8"))
        print("wrote", args.variants_out, os.path.getsize(args.variants_out), "bytes")
        return

    rng = random.Random(SEED)
    result = {
        "freetype": {"version": "%d.%d.%d" % version, "tag": "VER-2-14-3"},
        "format": "per glyph: a=advance (26.6), e=contour end points, x/y=coordinates (26.6), t=1 for an on-curve point and 2 for a control point of "
                  "a cubic curve, err=FreeType's error; per location: the normalized vector FreeType kept (16.16, one for each axis)",
        "fonts": [],
    }

    fixtures = [("HintingCff2.otf", build_multi_font), ("HintingCff2Single.otf", build_single_font), ("HintingCff2NoAxes.otf", build_noaxes_font)]
    built = {}
    for file_name, builder in fixtures:
        data, n_glyphs = builder(rng)
        with open(os.path.join(HERE, file_name), "wb") as f:
            f.write(data)
        print("wrote", file_name, len(data), "bytes")
        built[file_name] = (data, n_glyphs)
        entry = record_font(freetype, raw, file_name, data, n_glyphs)
        report(entry)
        result["fonts"].append(entry)

    # the bundled real variable CFF2 font: no hints, HVAR moves the advances
    with open(os.path.join(HERE, "VariableCff2Test.otf"), "rb") as f:
        data = f.read()
    entry = record_font(freetype, raw, "VariableCff2Test.otf", data, 8, check_fresh=False)
    report(entry)
    result["fonts"].append(entry)

    with gzip.GzipFile(args.out, "wb", mtime=0) as f:
        f.write(json.dumps(result, separators=(",", ":")).encode("utf-8"))
    print("wrote", args.out, os.path.getsize(args.out), "bytes")

    small = record_variants(freetype, raw, random.Random(SEED + args.mutant_seed), args.mutants)
    with gzip.GzipFile(args.variants_out, "wb", mtime=0) as f:
        f.write(json.dumps(small, separators=(",", ":")).encode("utf-8"))
    print("wrote", args.variants_out, os.path.getsize(args.variants_out), "bytes")


if __name__ == "__main__":
    main()
