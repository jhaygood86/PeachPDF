#!/usr/bin/env python3
"""Generates the synthetic CFF fonts of the hinting tests (HintingCff*.otf) and HintingCff.golden.json.gz: what FreeType itself makes of
every glyph of them (and of the bundled CFF fonts), for the port of Adobe's CFF engine (PeachDrawing.Text/Internal/Hinting/FreeType) to be
compared with exactly, in 26.6 units.

The bundled real CFF fonts exercise the operators and hints their designers used. These fixtures exercise every corner of the engine:
each glyph is a random Type 2 charstring with mostly sensible arguments (stem hints near the blue zones, hint masks and hint
substitution, every drawing operator, flex, subroutines, the arithmetic and storage operators, the accent composition of endchar with
four arguments) and a few nonsensical ones, and the fonts vary what the engine reads from the font: blue zones and family blues, StdHW and
StdVW, a LanguageGroup of 1 (the em box hints of ideographic fonts), a font matrix with a shear, and a CID-keyed font whose font
dictionaries have their own private dictionaries and matrices. A charstring that FreeType refuses is as good a test as one that works: the
port has to refuse it as well.

FreeType loads each glyph from a face of its own, because the `random` operator of the charstring language draws from a generator whose
state a glyph leaves for the next one (FreeType would otherwise make a glyph depend on the glyphs loaded before it), and the seed is set
to zero, which makes it the font's own (initialRandomSeed): what the port does.

The fonts are CC0 (they contain no third-party data; see HintingCff.LICENSE.txt). They are deterministic: a seed fixes every byte.

Usage (see generate_hinting_golden.py for --freetype):

  python generate_hinting_cff_fixtures.py --freetype path/to/freetype.dll

Requires freetype-py and fontTools.
"""
import argparse
import ctypes
import gzip
import io
import json
import os
import random
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_GOLDEN = os.path.join(HERE, "HintingCff.golden.json.gz")

SEED = 0xCFF2

# 26.6 sizes recorded
SIZES = [9 * 64, 11 * 64 + 32, 16 * 64, 30 * 64]

# (name, file, glyph limit): the bundled CFF fonts that are compared besides the fixtures
BUNDLED = [
    ("SourceCodePro-Regular.otf", 140),
    ("gsubtest-lookup3.otf", 60),
]

FT_LOAD_NO_BITMAP = 0x8
FT_LOAD_NO_AUTOHINT = 0x8000
FT_LOAD_TARGET_MONO = 2 << 16

# Type 2 operators
HSTEM, VSTEM, VMOVETO, RLINETO, HLINETO, VLINETO, RRCURVETO = 1, 3, 4, 5, 6, 7, 8
CALLSUBR, RETURN, ESCAPE, ENDCHAR = 10, 11, 12, 14
HSTEMHM, HINTMASK, CNTRMASK, RMOVETO, HMOVETO, VSTEMHM = 18, 19, 20, 21, 22, 23
RCURVELINE, RLINECURVE, VVCURVETO, HHCURVETO, SHORTINT, CALLGSUBR, VHCURVETO, HVCURVETO = 24, 25, 26, 27, 28, 29, 30, 31
# escaped operators (12 n)
AND, OR, NOT, ABS, ADD, SUB, DIV, NEG, EQ, DROP, PUT, GET, IFELSE, RANDOM, MUL, SQRT, DUP, EXCH, INDEX, ROLL = (
    3, 4, 5, 9, 10, 11, 12, 14, 15, 18, 20, 21, 22, 23, 24, 26, 27, 28, 29, 30)
HFLEX, FLEX, HFLEX1, FLEX1 = 34, 35, 36, 37


def num(v):
    """A charstring number: an integer (1, 2, 3 or 5 bytes) or, when not whole, a 16.16 fixed number."""
    if isinstance(v, float) and v != int(v):
        f = int(round(v * 65536)) & 0xFFFFFFFF
        return bytes([255]) + struct.pack(">I", f)
    v = int(v)
    if -107 <= v <= 107:
        return bytes([v + 139])
    if 108 <= v <= 1131:
        v -= 108
        return bytes([247 + (v >> 8), v & 0xFF])
    if -1131 <= v <= -108:
        v = -v - 108
        return bytes([251 + (v >> 8), v & 0xFF])
    if -32768 <= v <= 32767:
        return bytes([SHORTINT]) + struct.pack(">h", v)
    return bytes([255]) + struct.pack(">i", v << 16 if abs(v) < 32768 else v)


def op(code, esc=False):
    return bytes([ESCAPE, code]) if esc else bytes([code])


class Glyph:
    """Builds one random charstring."""

    def __init__(self, rng, font):
        self.rng = rng
        self.font = font
        self.out = bytearray()
        self.stems = 0
        self.have_width = False

    def emit(self, *parts):
        for p in parts:
            self.out += p

    def width_arg(self):
        # the first stack-clearing operator takes the width, as a difference from nominalWidthX, half of the time
        if not self.have_width:
            self.have_width = True
            if self.rng.random() < 0.5:
                return [num(self.rng.randint(-200, 400))]
        return []

    def y_near_blue(self):
        r = self.rng
        blues = self.font["blue_edges"]
        if blues and r.random() < 0.65:
            return r.choice(blues) + r.randint(-6, 6)
        return r.randint(-220, 900)

    def hints(self, vertical):
        r = self.rng
        count = r.choice([0, 1, 1, 2, 2, 3, 4, 6, 9]) if not vertical else r.choice([0, 0, 1, 2, 3])
        if count == 0:
            return b"", 0
        edges = sorted(self.y_near_blue() if not vertical else r.randint(-50, 700) for _ in range(count * 2))
        args = []
        prev = 0
        for i in range(count):
            bottom, top = edges[2 * i], edges[2 * i + 1]
            kind = r.random()
            if kind < 0.05:
                width = -21  # ghost bottom
            elif kind < 0.10:
                width = -20  # ghost top
            elif kind < 0.13:
                width = -(top - bottom) or -5  # an inverted pair
            else:
                width = top - bottom
            args += [num(bottom - prev), num(width)]
            prev = bottom + width
        return b"".join(args), count

    def path(self, ops):
        r = self.rng
        x = y = 0

        def rnd(a=-180, b=180):
            return r.randint(a, b)

        for _ in range(ops):
            choice = r.random()
            if choice < 0.14:
                self.emit(*[num(rnd()) for _ in range(2 * r.randint(1, 4))], op(RLINETO))
            elif choice < 0.24:
                self.emit(*[num(rnd()) for _ in range(r.randint(1, 5))], op(r.choice([HLINETO, VLINETO])))
            elif choice < 0.42:
                self.emit(*[num(rnd()) for _ in range(6 * r.randint(1, 3))], op(RRCURVETO))
            elif choice < 0.50:
                extra = [num(rnd())] if r.random() < 0.4 else []
                self.emit(*extra, *[num(rnd()) for _ in range(4 * r.randint(1, 2))], op(r.choice([HHCURVETO, VVCURVETO])))
            elif choice < 0.60:
                n = 4 * r.randint(1, 3) + r.choice([0, 0, 1])
                self.emit(*[num(rnd()) for _ in range(n)], op(r.choice([HVCURVETO, VHCURVETO])))
            elif choice < 0.66:
                self.emit(*[num(rnd()) for _ in range(6 * r.randint(0, 2) + 2)], op(RCURVELINE))
            elif choice < 0.72:
                self.emit(*[num(rnd()) for _ in range(2 * r.randint(0, 2) + 6)], op(RLINECURVE))
            elif choice < 0.77:
                # flex
                kind = r.choice(["flex", "hflex", "hflex1", "flex1"])
                if kind == "flex":
                    self.emit(*[num(rnd(-60, 60)) for _ in range(12)], num(50), op(FLEX, True))
                elif kind == "hflex":
                    self.emit(*[num(rnd(-60, 60)) for _ in range(7)], op(HFLEX, True))
                elif kind == "hflex1":
                    self.emit(*[num(rnd(-60, 60)) for _ in range(9)], op(HFLEX1, True))
                else:
                    self.emit(*[num(rnd(-60, 60)) for _ in range(11)], op(FLEX1, True))
            elif choice < 0.85:
                self.arithmetic()
            elif choice < 0.90:
                self.subr_call()
            elif choice < 0.93:
                # substitute hints in the middle of the path
                self.hint_mask()
            elif choice < 0.97:
                self.emit(*[num(rnd()) for _ in range(r.randint(0, 4))], op(r.choice([HMOVETO, VMOVETO, RMOVETO])))
            else:
                # a number that is not whole (a fixed number)
                self.emit(num(rnd() + 0.5), num(rnd()), op(RLINETO))

    def arithmetic(self):
        r = self.rng
        a, b = r.randint(-90, 90), r.randint(-90, 90)
        kind = r.choice(["add", "sub", "mul", "div", "neg", "abs", "sqrt", "dup", "exch", "put", "ifelse", "and", "or", "not", "eq",
                         "index", "roll", "drop", "random", "get"])
        if kind in ("add", "sub", "mul", "div", "and", "or", "eq", "exch"):
            code = {"add": ADD, "sub": SUB, "mul": MUL, "div": DIV, "and": AND, "or": OR, "eq": EQ, "exch": EXCH}[kind]
            if kind == "div" and b == 0:
                b = 3
            self.emit(num(a), num(b), op(code, True))
            self.emit(op(DROP, True))
            self.emit(num(r.randint(-80, 80)), num(r.randint(-80, 80)), op(RLINETO))
        elif kind in ("neg", "abs", "sqrt", "dup", "not"):
            code = {"neg": NEG, "abs": ABS, "sqrt": SQRT, "dup": DUP, "not": NOT}[kind]
            self.emit(num(abs(a) if kind == "sqrt" else a), op(code, True))
            if kind == "dup":
                self.emit(op(RLINETO))
            else:
                self.emit(num(r.randint(-80, 80)), op(RLINETO))
        elif kind == "put":
            slot = r.randint(0, 33)
            self.emit(num(a), num(slot), op(PUT, True), num(slot), op(GET, True), num(r.randint(-80, 80)), op(RLINETO))
        elif kind == "ifelse":
            self.emit(num(a), num(b), num(r.randint(-5, 5)), num(r.randint(-5, 5)), op(IFELSE, True), num(r.randint(-80, 80)), op(RLINETO))
        elif kind == "index":
            self.emit(num(a), num(b), num(r.randint(-100, 100)), num(r.randint(0, 3)), op(INDEX, True), op(RLINETO))
        elif kind == "roll":
            self.emit(*[num(r.randint(-80, 80)) for _ in range(4)], num(4), num(r.randint(-3, 3)), op(ROLL, True), op(RLINETO))
            self.emit(op(RLINETO))
        elif kind == "drop":
            self.emit(num(a), op(DROP, True))
        elif kind == "random":
            self.emit(op(RANDOM, True), num(30), op(RLINETO))
        else:
            self.emit(num(r.randint(0, 40)), op(GET, True), num(20), op(RLINETO))

    def subr_call(self):
        r = self.rng
        font = self.font
        if font["local_subrs"] and r.random() < 0.6:
            n = len(font["local_subrs"])
            self.emit(num(r.randrange(n) - bias(n)), op(CALLSUBR))
        elif font["global_subrs"]:
            n = len(font["global_subrs"])
            self.emit(num(r.randrange(n) - bias(n)), op(CALLGSUBR))

    def hint_mask(self):
        r = self.rng
        count = self.stems
        nbytes = (count + 7) // 8
        if nbytes == 0:
            return
        mask = bytearray(r.getrandbits(8) for _ in range(nbytes))
        # the unused bits of the last byte are zero, as the specification asks
        if count % 8:
            mask[-1] &= (0xFF << (8 - count % 8)) & 0xFF
        self.emit(op(r.choice([HINTMASK, HINTMASK, HINTMASK, CNTRMASK])), bytes(mask))

    def make(self):
        r = self.rng
        # hints
        hargs, hcount = self.hints(False)
        first = self.width_arg()
        if hcount:
            self.emit(*first, hargs, op(r.choice([HSTEM, HSTEMHM])))
            self.stems += hcount
        vargs, vcount = self.hints(True)
        if vcount:
            if r.random() < 0.5:
                self.emit(vargs, op(r.choice([VSTEM, VSTEMHM])))
                self.stems += vcount
                self.hint_mask()
            else:
                # the vertical stems as the implicit stems of a hintmask
                self.emit(vargs)
                self.stems += vcount
                nbytes = (self.stems + 7) // 8
                mask = bytearray(r.getrandbits(8) for _ in range(nbytes))
                if self.stems % 8:
                    mask[-1] &= (0xFF << (8 - self.stems % 8)) & 0xFF
                self.emit(op(HINTMASK), bytes(mask))
        elif self.stems and r.random() < 0.8:
            self.hint_mask()

        contours = r.choice([1, 1, 2, 2, 3, 4])
        for _ in range(contours):
            m = r.random()
            pw = self.width_arg()
            if m < 0.4:
                self.emit(*pw, num(r.randint(-40, 400)), num(self.y_near_blue()), op(RMOVETO))
            elif m < 0.7:
                self.emit(*pw, num(r.randint(-40, 400)), op(HMOVETO))
            else:
                self.emit(*pw, num(self.y_near_blue()), op(VMOVETO))
            self.path(r.randint(2, 9))

        self.emit(*self.width_arg(), op(ENDCHAR))
        return bytes(self.out)


def bias(n):
    return 107 if n < 1240 else 1131 if n < 33900 else 32768


def random_subr(rng, font, kind):
    """A subroutine: a piece of path, or hints, or a call of another one, ending with return."""
    g = Glyph(rng, font)
    g.have_width = True
    if kind == "hints":
        args, count = g.hints(False)
        if count:
            g.emit(args, op(HSTEMHM))
    else:
        g.path(rng.randint(1, 4))
    g.emit(op(RETURN))
    return bytes(g.out)


STANDARD_NAMES = None


def standard_names():
    global STANDARD_NAMES
    if STANDARD_NAMES is None:
        from fontTools.cffLib import cffStandardStrings
        STANDARD_NAMES = [n for n in cffStandardStrings[:229]]
    return STANDARD_NAMES


def build_font(kind, rng):
    """Builds one fixture font: returns (bytes, glyph count)."""
    from fontTools.fontBuilder import FontBuilder
    from fontTools.misc.psCharStrings import T2CharString
    from fontTools.ttLib import TTFont

    upem = 1000
    latin = kind in ("latin", "matrix", "cid")
    if kind == "latin":
        private = {
            "BlueValues": [-15, 0, 486, 500, 700, 715, 730, 745],
            "OtherBlues": [-235, -220],
            "FamilyBlues": [-14, 0, 487, 500, 700, 714],
            "FamilyOtherBlues": [-236, -221],
            "BlueScale": 0.039625, "BlueShift": 7, "BlueFuzz": 1,
            "StdHW": 68, "StdVW": 84, "defaultWidthX": 500, "nominalWidthX": 560,
        }
    elif kind == "ideo":
        private = {"LanguageGroup": 1, "BlueValues": [], "StdHW": 40, "StdVW": 100, "defaultWidthX": 1000, "nominalWidthX": 900}
    elif kind == "matrix":
        private = {
            "BlueValues": [-12, 0, 480, 494, 690, 704],
            "BlueScale": 0.05, "BlueShift": 5, "BlueFuzz": 2,
            "StdHW": 60, "StdVW": 70, "defaultWidthX": 480, "nominalWidthX": 520,
        }
    else:  # cid: overridden per font dictionary below
        private = {"BlueValues": [-15, 0, 486, 500, 700, 715], "StdHW": 68, "StdVW": 84, "defaultWidthX": 500, "nominalWidthX": 560}

    edges = []
    for k in ("BlueValues", "OtherBlues"):
        edges += private.get(k, [])
    font_ctx = {"blue_edges": edges, "local_subrs": [], "global_subrs": []}

    names = [".notdef"] + standard_names()[1:] + ["g%d" % i for i in range(229, 229 + 71)]
    n_glyphs = len(names)

    font_ctx["local_subrs"] = [random_subr(rng, font_ctx, "hints" if i % 5 == 0 else "path") for i in range(12)]
    font_ctx["global_subrs"] = [random_subr(rng, font_ctx, "hints" if i % 6 == 0 else "path") for i in range(8)]

    charstrings = {}
    for i, name in enumerate(names):
        if i == 0:
            g = Glyph(rng, font_ctx)
            g.emit(num(500), op(HMOVETO), num(100), num(0), op(RLINETO), op(ENDCHAR))
            data = bytes(g.out)
        else:
            data = Glyph(rng, font_ctx).make()
            # some accented glyphs: endchar with four arguments (the accent composition of the old seac operator)
            if name in ("Aacute", "Eacute", "Otilde", "acircumflex", "eacute", "udieresis", "ntilde") and rng.random() < 0.9:
                base = {"Aacute": 65, "Eacute": 69, "Otilde": 79, "acircumflex": 97, "eacute": 101, "udieresis": 117, "ntilde": 110}[name]
                accent = {"Aacute": 194, "Eacute": 194, "Otilde": 196, "acircumflex": 195, "eacute": 194, "udieresis": 200, "ntilde": 196}[name]
                data = num(rng.randint(-30, 30)) + num(rng.randint(-30, 30)) + num(base) + num(accent) + op(ENDCHAR)
        cs = T2CharString(bytecode=data)
        charstrings[name] = cs

    fb = FontBuilder(upem, isTTF=False)
    # the glyphs are not drawn to compute bounding boxes: many of them are nonsense that fontTools could not follow
    fb.font.recalcBBoxes = False
    fb.setupGlyphOrder(names)
    fb.setupCharacterMap({0x20 + i: names[i] for i in range(1, 95) if i < len(names)})
    font_info = {"FullName": "HintingCff " + kind, "FamilyName": "HintingCff", "Weight": "Regular", "FontBBox": [-200, -300, 1200, 1000]}
    fb.setupCFF("HintingCff-" + kind, font_info, charstrings, dict(private))
    fb.setupHorizontalMetrics({n: (500, 0) for n in names})
    fb.setupHorizontalHeader(ascent=800, descent=-200)
    fb.setupNameTable({"familyName": "HintingCff", "styleName": kind})
    fb.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
    fb.setupPost()

    font = fb.font
    cff = font["CFF "].cff
    top = cff.topDictIndex[0]
    from fontTools.cffLib import SubrsIndex, GlobalSubrsIndex

    # subroutines
    gs = cff.GlobalSubrs
    for data in font_ctx["global_subrs"]:
        gs.append(T2CharString(bytecode=data))
    ls = SubrsIndex()
    for data in font_ctx["local_subrs"]:
        ls.append(T2CharString(bytecode=data))
    top.Private.Subrs = ls

    if kind == "matrix":
        # a font matrix with a shear: the outline is transformed after hinting
        top.FontMatrix = [0.001, 0.0, 0.0002, 0.001, 0.0, 0.0]

    buf = io.BytesIO()
    font.save(buf)
    return buf.getvalue(), n_glyphs


def build_cid_font(rng):
    """A CID-keyed fixture: three font dictionaries, each with its own private dictionary (blue zones, StdHW, LanguageGroup) and local
    subroutines, and font matrices of their own (a different scale, a shear), chosen per glyph by an FDSelect of format 3."""
    from fontTools.fontBuilder import FontBuilder
    from fontTools.misc.psCharStrings import T2CharString
    from fontTools.cffLib import CharStrings, PrivateDict, FDArrayIndex, FontDict, SubrsIndex, FDSelect

    n_glyphs = 150
    names = [".notdef"] + ["cid%05d" % i for i in range(1, n_glyphs)]
    privs = [
        {"BlueValues": [-15, 0, 486, 500, 700, 715], "OtherBlues": [-235, -220], "StdHW": 68, "StdVW": 84, "defaultWidthX": 500, "nominalWidthX": 560},
        {"BlueValues": [-10, 0, 470, 480, 690, 700], "BlueScale": 0.05, "BlueShift": 5, "StdHW": 50, "StdVW": 90, "defaultWidthX": 600, "nominalWidthX": 500},
        {"LanguageGroup": 1, "BlueValues": [], "StdHW": 40, "StdVW": 100, "defaultWidthX": 1000, "nominalWidthX": 800},
    ]
    matrices = [None, [0.0005, 0, 0, 0.0005, 0, 0], [0.001, 0, 0.0001, 0.001, 0, 0]]
    select = [0 if i < 50 else 1 if i < 100 else 2 for i in range(n_glyphs)]

    contexts = []
    for k in range(3):
        edges = privs[k].get("BlueValues", []) + privs[k].get("OtherBlues", [])
        ctx = {"blue_edges": edges, "local_subrs": [], "global_subrs": []}
        ctx["local_subrs"] = [random_subr(rng, ctx, "hints" if i % 5 == 0 else "path") for i in range(8)]
        contexts.append(ctx)
    global_subrs = [random_subr(rng, contexts[0], "hints" if i % 6 == 0 else "path") for i in range(8)]
    for ctx in contexts:
        ctx["global_subrs"] = global_subrs

    data = {}
    for i, name in enumerate(names):
        if i == 0:
            data[name] = T2CharString(bytecode=num(500) + op(HMOVETO) + op(ENDCHAR))
        else:
            data[name] = T2CharString(bytecode=Glyph(rng, contexts[select[i]]).make())

    fb = FontBuilder(1000, isTTF=False)
    fb.font.recalcBBoxes = False
    fb.setupGlyphOrder(names)
    fb.setupCharacterMap({0x20 + i: names[i] for i in range(1, 60)})
    fb.setupCFF("HintingCff-cid", {"FullName": "HintingCff cid", "FamilyName": "HintingCff", "Weight": "Regular", "FontBBox": [-200, -300, 1200, 1000]},
                data, {"BlueValues": []})
    fb.setupHorizontalMetrics({n: (500, 0) for n in names})
    fb.setupHorizontalHeader(ascent=800, descent=-200)
    fb.setupNameTable({"familyName": "HintingCff", "styleName": "cid"})
    fb.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
    fb.setupPost()

    font = fb.font
    cff = font["CFF "].cff
    top = cff.topDictIndex[0]
    gs = cff.GlobalSubrs
    for subr in global_subrs:
        gs.append(T2CharString(bytecode=subr))

    fd_array = FDArrayIndex()
    fd_array.strings = None
    fd_array.GlobalSubrs = gs
    for k in range(3):
        fd = FontDict()
        private = PrivateDict()
        for key, value in privs[k].items():
            setattr(private, key, value)
        subrs = SubrsIndex()
        for subr in contexts[k]["local_subrs"]:
            subrs.append(T2CharString(bytecode=subr))
        private.Subrs = subrs
        fd.Private = private
        fd.FontName = "FD%d" % k
        if matrices[k]:
            fd.FontMatrix = matrices[k]
        fd_array.append(fd)

    fd_select = FDSelect()
    fd_select.format = 3
    fd_select.gidArray = select
    top.ROS = ("Adobe", "Identity", 0)
    top.CIDCount = n_glyphs
    top.FDArray = fd_array
    top.FDSelect = fd_select
    del top.Private
    top.charset = names
    top.CharStrings = CharStrings(None, names, gs, None, fd_select, fd_array)
    for i, name in enumerate(names):
        top.CharStrings[name] = data[name]
        data[name].private = fd_array[select[i]].Private
        data[name].globalSubrs = gs

    buf = io.BytesIO()
    font.save(buf)
    return buf.getvalue(), n_glyphs


def build_hostile_font():
    """A font of hostile glyphs: charstrings a well-behaved font never has, which the engine has to answer with an error or with what
    FreeType makes of them, in bounded time (FreeType is compared with for what it does). Glyph 1 is `A` and glyph 2 `acute`."""
    from fontTools.fontBuilder import FontBuilder
    from fontTools.misc.psCharStrings import T2CharString
    from fontTools.cffLib import SubrsIndex

    def call(n):
        return num(n - 107) + op(CALLSUBR)

    line = num(10) + num(5) + op(RLINETO)
    start = num(50) + num(50) + op(RMOVETO)

    subrs = []
    subrs.append(call(0) + op(RETURN))                                   # subr 0 calls itself
    for k in range(1, 13):                                               # subrs 1..12: each calls the next 30 times; the last draws
        subrs.append((call(k + 1) * 30 if k < 12 else line * 4) + op(RETURN))
    subrs.append(line + op(RETURN))                                      # subr 13 draws

    glyphs = {}
    glyphs[".notdef"] = num(500) + op(HMOVETO) + op(ENDCHAR)
    glyphs["A"] = num(0) + num(0) + num(65) + num(194) + op(ENDCHAR)     # an accent composition of itself with `acute`
    glyphs["acute"] = start + line + line + line + op(ENDCHAR)
    glyphs["g3"] = start + call(0) + op(ENDCHAR)                         # a subroutine that calls itself
    glyphs["g4"] = start + call(1) + op(ENDCHAR)                         # 30^12 calls: the instruction limit ends it
    glyphs["g5"] = b"".join(num(y) + num(10) + op(HSTEMHM) for y in range(0, 5)) * 0 + b"".join(
        b"".join(num(10) + num(5) for _ in range(20)) + op(HSTEMHM) for _ in range(5)) + start + line + op(ENDCHAR)   # 100 hints
    glyphs["g6"] = start + b"".join(num(i) for i in range(60)) + op(RLINETO) + op(ENDCHAR)                    # 60 operands: stack overflow
    glyphs["g7"] = start + (b"".join(num(i % 50) for i in range(40)) + op(RLINETO)) * 900 + op(ENDCHAR)      # 18,000 points
    glyphs["g8"] = start + (b"".join(num(i % 50) for i in range(40)) + op(RLINETO)) * 2000 + op(ENDCHAR)     # 40,000 points: too many
    glyphs["g9"] = num(10) + num(20) + op(HSTEMHM) + op(HINTMASK) + bytes([0xFF, 0xFF, 0xFF]) + start + line + op(ENDCHAR)  # a mask too long
    glyphs["g10"] = start + b"".join(num(i) for i in range(30)) + num(30) + num(1000000) + op(ROLL, True) + op(RLINETO) + op(ENDCHAR)
    glyphs["g11"] = start + num(1) + num(0) + op(DIV, True) + num(0) + op(RLINETO) + op(ENDCHAR)             # a division by zero
    glyphs["g12"] = start + num(7) + op(HFLEX, True) + op(ENDCHAR)                                            # too few operands
    glyphs["g13"] = start + num(-5000) + op(CALLSUBR) + op(ENDCHAR)                                           # a subroutine that is not there
    glyphs["g14"] = start + line + op(RETURN) + op(ENDCHAR)                                                   # return from the top level
    glyphs["g15"] = start + num(3) + num(2) + op(SUB, True) + op(SQRT, True) + op(ABS, True) + num(-2000000) + op(NEG, True) + op(RLINETO) + op(ENDCHAR)
    glyphs["g16"] = start + b"".join(num(70000 + i) for i in range(2)) + op(RLINETO) + op(ENDCHAR)            # numbers of 5 bytes
    glyphs["g17"] = start + call(13) * 3000 + op(ENDCHAR)                                                     # 3,000 calls of a small subroutine
    names = list(glyphs)

    fb = FontBuilder(1000, isTTF=False)
    fb.font.recalcBBoxes = False
    fb.setupGlyphOrder(names)
    fb.setupCharacterMap({0x41: "A"})
    charstrings = {n: T2CharString(bytecode=glyphs[n]) for n in names}
    fb.setupCFF("HintingCff-hostile", {"FullName": "HintingCff hostile", "FamilyName": "HintingCff", "Weight": "Regular", "FontBBox": [-200, -300, 1200, 1000]},
                charstrings, {"BlueValues": [-15, 0, 486, 500, 700, 715], "StdHW": 68, "StdVW": 84, "defaultWidthX": 500, "nominalWidthX": 560})
    fb.setupHorizontalMetrics({n: (500, 0) for n in names})
    fb.setupHorizontalHeader(ascent=800, descent=-200)
    fb.setupNameTable({"familyName": "HintingCff", "styleName": "hostile"})
    fb.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
    fb.setupPost()

    font = fb.font
    cff = font["CFF "].cff
    top = cff.topDictIndex[0]
    ls = SubrsIndex()
    for data in subrs:
        ls.append(T2CharString(bytecode=data))
    top.Private.Subrs = ls

    buf = io.BytesIO()
    font.save(buf)
    return buf.getvalue(), len(names)


def load_freetype(path):
    """Makes freetype-py use a specific FreeType shared library instead of the one it bundles."""
    if path:
        real = ctypes.CDLL

        def patched(name, *args, **kwargs):
            if isinstance(name, str) and os.path.basename(name).lower().startswith("libfreetype"):
                name = os.path.abspath(path)
            return real(name, *args, **kwargs)

        ctypes.CDLL = patched

    import freetype
    from freetype import raw
    return freetype, raw


def sfnt_bytes(path):
    with open(path, "rb") as f:
        data = f.read()
    if data[:4] in (b"wOFF", b"wOF2"):
        from fontTools.ttLib import TTFont
        font = TTFont(io.BytesIO(data))
        font.flavor = None
        out = io.BytesIO()
        font.save(out)
        return out.getvalue()
    return data


def record(freetype, raw, data, glyphs, modes):
    """What FreeType makes of each glyph, for every mode and size: a fresh library and face for each glyph."""
    cbuf = ctypes.create_string_buffer(data, len(data))
    result = {}
    for mode_name, (mono, darken, sizes) in modes.items():
        runs = []
        for size in sizes:
            per_glyph = {}
            for gid in glyphs:
                lib = freetype.FT_Library()
                assert raw.FT_Init_FreeType(ctypes.byref(lib)) == 0
                seed = ctypes.c_int(0)
                assert raw._lib.FT_Property_Set(lib, b"cff", b"random-seed", ctypes.byref(seed)) == 0
                engine = ctypes.c_uint(1)  # FT_HINTING_ADOBE
                assert raw._lib.FT_Property_Set(lib, b"cff", b"hinting-engine", ctypes.byref(engine)) == 0
                dark = ctypes.c_int(0 if darken else 1)
                assert raw._lib.FT_Property_Set(lib, b"cff", b"no-stem-darkening", ctypes.byref(dark)) == 0
                face = freetype.FT_Face()
                assert raw.FT_New_Memory_Face(lib, cbuf, len(data), 0, ctypes.byref(face)) == 0
                assert raw.FT_Set_Char_Size(face, size, size, 72, 72) == 0
                flags = FT_LOAD_NO_BITMAP | FT_LOAD_NO_AUTOHINT | (FT_LOAD_TARGET_MONO if mono else 0)
                error = raw.FT_Load_Glyph(face, gid, flags)
                if error:
                    per_glyph[str(gid)] = {"err": error}
                else:
                    slot = face.contents.glyph.contents
                    outline = slot.outline
                    # the counts are unsigned 16-bit numbers, which ctypes reads as signed
                    n = outline.n_points & 0xFFFF
                    per_glyph[str(gid)] = {
                        "a": slot.advance.x,
                        "e": [outline.contours[i] & 0xFFFF for i in range(outline.n_contours & 0xFFFF)],
                        "x": [outline.points[i].x for i in range(n)],
                        "y": [outline.points[i].y for i in range(n)],
                        "t": [outline.tags[i] & 3 for i in range(n)],
                    }
                raw.FT_Done_Face(face)
                raw.FT_Done_FreeType(lib)
            runs.append({"size": size, "glyphs": per_glyph})
        result[mode_name] = runs
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--freetype", help="path to a FreeType shared library built from the tag the port derives from")
    parser.add_argument("--allow-other-version", action="store_true")
    parser.add_argument("--out", default=OUT_GOLDEN)
    args = parser.parse_args()

    freetype, raw = load_freetype(args.freetype)
    version = freetype.version()
    if version != (2, 14, 3) and not args.allow_other_version:
        sys.exit("FreeType %d.%d.%d loaded; the port derives from 2.14.3 (see --freetype)" % version)

    rng = random.Random(SEED)
    modes = {
        # the outlines of Adobe's engine do not depend on the render mode: both are recorded to say so
        "standard": (False, False, SIZES),
        "monochrome": (True, False, [11 * 64 + 32]),
        # with the stem darkening of the engine on (it is off by default)
        "darkened": (False, True, [9 * 64, 16 * 64]),
    }

    result = {
        "freetype": {"version": "%d.%d.%d" % version, "tag": "VER-2-14-3"},
        "format": "per glyph: a=advance (26.6), e=contour end points, x/y=coordinates (26.6), t=1 for an on-curve point and 2 for a control point of a cubic curve, err=FreeType's error",
        "fonts": [],
    }

    fixtures = [("latin", "HintingCff.otf"), ("ideo", "HintingCffIdeo.otf"), ("matrix", "HintingCffMatrix.otf")]
    fixtures.append(("cid", "HintingCffCid.otf"))
    for kind, file_name in fixtures:
        data, n_glyphs = build_cid_font(rng) if kind == "cid" else build_font(kind, rng)
        with open(os.path.join(HERE, file_name), "wb") as f:
            f.write(data)
        print("wrote", file_name, len(data), "bytes")
        result["fonts"].append({"file": file_name, "modes": record(freetype, raw, data, list(range(0, n_glyphs)), modes)})

    data, n_glyphs = build_hostile_font()
    with open(os.path.join(HERE, "HintingCffHostile.otf"), "wb") as f:
        f.write(data)
    print("wrote HintingCffHostile.otf", len(data), "bytes")
    result["fonts"].append({"file": "HintingCffHostile.otf", "modes": record(freetype, raw, data, list(range(0, n_glyphs)), {"standard": (False, False, [16 * 64])})})

    for file_name, limit in BUNDLED:
        data = sfnt_bytes(os.path.join(HERE, file_name))
        result["fonts"].append({"file": file_name, "modes": record(freetype, raw, data, list(range(0, limit)), modes)})
        print(file_name, "recorded")

    with gzip.GzipFile(args.out, "wb", mtime=0) as f:
        f.write(json.dumps(result, separators=(",", ":")).encode("utf-8"))
    print("wrote", args.out, os.path.getsize(args.out), "bytes")


if __name__ == "__main__":
    main()
