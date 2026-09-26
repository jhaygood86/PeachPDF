#!/usr/bin/env python3
"""Generates HintingOpcodes.ttf and HintingOpcodes.golden.json.gz: a synthetic TrueType font whose glyphs run hundreds of random
TrueType programs, and what FreeType itself makes of every one, for the port of FreeType's bytecode interpreter to be compared
with exactly (in 26.6 units).

The real fonts of assets/fonts (see generate_hinting_golden.py) exercise the instructions their designers used. This fixture
exercises every instruction and every corner of the graphics state, including the behaviour on invalid arguments: each glyph's
program is a random sequence of instructions with mostly sensible arguments (point numbers of the glyph, control value indices,
plausible distances), a few nonsensical ones, IF/ELSE blocks, forward jumps, calls of functions and instructions defined in the font
program, and loops; every opcode is used at least once through a table that walks all of them. Composite glyphs put simple glyphs
together with every kind of component argument (offsets, matched points, scaled and unscaled offsets, all three transform forms,
rounding to the grid, USE_MY_METRICS) and carry instructions of their own. FreeType (which ignores the error of a glyph program
unless it is asked to be pedantic) keeps what a program had done when it failed, and so the port has to as well; a glyph program that
fails is as good a test as one that runs to the end.

The font is CC0 (it contains no third-party data; see HintingOpcodes.LICENSE.txt). It is deterministic: a seed fixes every byte.

Usage (see generate_hinting_golden.py for --freetype):

  python generate_hinting_opcode_fixtures.py --freetype path/to/freetype.dll

Requires freetype-py and fontTools.
"""
import argparse
import ctypes
import gzip
import io
import json
import math
import os
import random
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_FONT = os.path.join(HERE, "HintingOpcodes.ttf")
OUT_GOLDEN = os.path.join(HERE, "HintingOpcodes.golden.json.gz")

SEED = 0x7E57
SIMPLE_GLYPHS = 300
COMPOSITE_GLYPHS = 90
CVT_COUNT = 24
STORE_COUNT = 16
TWILIGHT = 12

# 26.6 sizes recorded, and the modes (see generate_hinting_golden.py)
SIZES = [11 * 64, 12 * 64 + 32, 24 * 64]
MODES = {
    "standard": (40, False, SIZES),
    "monochrome": (35, True, SIZES),
    "v40mono": (40, True, [12 * 64 + 32]),
    "v35normal": (35, False, [12 * 64 + 32]),
}

FT_LOAD_NO_BITMAP = 0x8
FT_LOAD_NO_AUTOHINT = 0x8000
FT_LOAD_TARGET_MONO = 2 << 16

# component flags
ROUND_XY_TO_GRID = 0x0004
USE_MY_METRICS = 0x0200
SCALED_COMPONENT_OFFSET = 0x0800
UNSCALED_COMPONENT_OFFSET = 0x1000


# ---------------------------------------------------------------------------------------------------------------------------
# Bytecode assembly
# ---------------------------------------------------------------------------------------------------------------------------

def push(values, rng=None):
    """Instructions that push values: PUSHB/NPUSHB when they are all small and unsigned, PUSHW/NPUSHW otherwise."""
    values = [max(-32768, min(32767, int(v))) for v in values]
    out = bytearray()
    i = 0
    while i < len(values):
        chunk = values[i:i + 255]
        if all(0 <= v <= 255 for v in chunk):
            out += bytes([0xB0 + len(chunk) - 1]) if len(chunk) <= 8 else bytes([0x40, len(chunk)])
            out += bytes(chunk)
        else:
            out += bytes([0xB8 + len(chunk) - 1]) if len(chunk) <= 8 else bytes([0x41, len(chunk)])
            for v in chunk:
                out += struct.pack(">h", v)
        i += 255
    return bytes(out)


class Gen:
    """Random program generator for one glyph: knows how many points the glyph has."""

    def __init__(self, rng, n_points, n_contours, functions, idef_opcodes):
        self.rng = rng
        self.n = n_points
        self.contours = n_contours
        self.functions = functions
        self.idefs = idef_opcodes

    # argument generators
    def point(self):
        r = self.rng.random()
        if r < 0.86:
            return self.rng.randrange(self.n)
        if r < 0.96:
            return self.n + self.rng.randrange(4)  # a phantom point
        return self.rng.choice([self.n + 4, 200, -1])  # nonsense

    def points(self, k):
        return [self.point() for _ in range(k)]

    def cvt(self):
        if self.rng.random() < 0.9:
            return self.rng.randrange(CVT_COUNT)
        return self.rng.choice([-1, CVT_COUNT, 500])

    def dist(self):
        return self.rng.choice([self.rng.randrange(-260, 260), self.rng.randrange(-40, 130), 0, 64, 32, 128, -64])

    def funits(self):
        return self.rng.randrange(-300, 900)

    def f2dot14(self):
        return self.rng.choice([0, 0x4000, -0x4000, 0x2D41, -0x2D41, self.rng.randrange(-0x4000, 0x4000)])

    def zone(self):
        return self.rng.choice([1, 1, 1, 0, 0, 2])

    def storage(self):
        return self.rng.randrange(STORE_COUNT + 2)

    def delta_entry(self):
        step = self.rng.randrange(16)
        base = self.rng.choice([0, 0, 0, 1, 2, 3, 4, 15])
        return (base << 4) | step

    # statements: each returns bytes; a statement pushes its own arguments
    def stmt(self):
        rng = self.rng
        table = [
            (6, self.s_vectors), (3, self.s_rounding), (5, self.s_state), (14, self.s_moves), (8, self.s_loops),
            (5, self.s_delta), (10, self.s_arith), (4, self.s_storage), (4, self.s_measure), (3, self.s_if),
            (3, self.s_call), (2, self.s_flip), (2, self.s_misc), (2, self.s_jump),
        ]
        pick = rng.randrange(sum(w for w, _ in table))
        for w, fn in table:
            if pick < w:
                return fn()
            pick -= w
        return b""

    def s_vectors(self):
        rng = self.rng
        c = rng.randrange(9)
        if c == 0:
            return bytes([rng.choice([0x00, 0x01, 0x02, 0x03, 0x04, 0x05])])
        if c == 1:
            return push(self.points(2)) + bytes([rng.choice([0x06, 0x07])])
        if c == 2:
            return push(self.points(2)) + bytes([rng.choice([0x08, 0x09])])
        if c == 3:
            return push([self.f2dot14(), self.f2dot14()]) + bytes([0x0A])
        if c == 4:
            return push([self.f2dot14(), self.f2dot14()]) + bytes([0x0B])
        if c == 5:
            return bytes([0x0E])
        if c == 6:
            return bytes([0x0C, 0x21, 0x21])  # GPV POP POP
        if c == 7:
            return push(self.points(2)) + bytes([rng.choice([0x86, 0x87])])
        return bytes([0x0D, 0x21, 0x21])

    def s_rounding(self):
        rng = self.rng
        c = rng.randrange(9)
        if c < 6:
            return bytes([[0x18, 0x19, 0x3D, 0x7C, 0x7D, 0x7A][c]])
        if c == 6:
            return push([rng.randrange(256)]) + bytes([0x76])
        if c == 7:
            return push([rng.randrange(256)]) + bytes([0x77])
        return push([self.dist()]) + bytes([rng.randrange(0x68, 0x70), 0x21])

    def s_state(self):
        rng = self.rng
        c = rng.randrange(10)
        if c == 0:
            return push([self.point()]) + bytes([rng.choice([0x10, 0x11, 0x12])])
        if c == 1:
            return push([self.zone()]) + bytes([rng.choice([0x13, 0x14, 0x15, 0x16])])
        if c == 2:
            return push([rng.choice([1, 2, 3, 5, 8, 100])]) + bytes([0x17])
        if c == 3:
            return push([self.dist()]) + bytes([rng.choice([0x1A, 0x1D, 0x1E])])
        if c == 4:
            return push([self.funits()]) + bytes([0x1F])
        if c == 5:
            return push([rng.randrange(0, 40)]) + bytes([0x5E])
        if c == 6:
            return push([rng.choice([0, 1, 2, 3, 4, 6, 7, -1])]) + bytes([0x5F])
        if c == 7:
            return push([rng.choice([0, 1, 4, 5, 8, 0x1FF, 0xFF, 0x0AFF, 0x1200, 0x2100])]) + bytes([0x85])
        if c == 8:
            return push([rng.choice([0, 1, 2, 4, 5, 6, -1])]) + bytes([0x8D])
        return push([rng.choice([1, 2, 3, 4, 5]), rng.choice([0, 1, 2, 3, 4, 5, 8])]) + bytes([0x8E])

    def s_moves(self):
        rng = self.rng
        c = rng.randrange(12)
        if c == 0:
            return push([self.point()]) + bytes([rng.choice([0x2E, 0x2F])])
        if c == 1:
            return push([self.point(), self.cvt()]) + bytes([rng.choice([0x3E, 0x3F])])
        if c == 2:
            return push([self.point()]) + bytes([0xC0 + rng.randrange(32)])
        if c == 3:
            return push([self.point(), self.cvt()]) + bytes([0xE0 + rng.randrange(32)])
        if c == 4:
            return push([self.point(), self.dist()]) + bytes([rng.choice([0x3A, 0x3B])])
        if c == 5:
            return push(self.points(2)) + bytes([0x27])
        if c == 6:
            return push([self.point()] + self.points(4)) + bytes([0x0F])
        if c == 7:
            return push([self.point(), self.dist()]) + bytes([0x48])
        if c == 8:
            return push([self.point()]) + bytes([0x29])
        if c == 9:
            return bytes([rng.choice([0x30, 0x31])])
        if c == 10:
            return push([rng.randrange(self.contours + 1)]) + bytes([rng.choice([0x34, 0x35])])
        return push([rng.choice([0, 1, 2])]) + bytes([rng.choice([0x36, 0x37])])

    def s_loops(self):
        rng = self.rng
        k = rng.randrange(1, 6)
        c = rng.randrange(5)
        loop = push([k]) + bytes([0x17])
        if c == 0:
            return loop + push(self.points(k)) + bytes([rng.choice([0x32, 0x33])])
        if c == 1:
            return loop + push(self.points(k) + [self.dist()]) + bytes([0x38])
        if c == 2:
            return loop + push(self.points(k)) + bytes([0x39])
        if c == 3:
            return loop + push(self.points(k)) + bytes([0x3C])
        return loop + push(self.points(k)) + bytes([0x80])

    def s_delta(self):
        rng = self.rng
        k = rng.randrange(1, 4)
        args = []
        for _ in range(k):
            args += [self.delta_entry(), self.point() if rng.random() < 0.7 else rng.randrange(CVT_COUNT)]
        code = rng.choice([0x5D, 0x71, 0x72, 0x73, 0x74, 0x75])
        return push(args + [k]) + bytes([code])

    def s_arith(self):
        rng = self.rng
        c = rng.randrange(8)
        if c == 0:
            two = [0x60, 0x61, 0x62, 0x63, 0x8B, 0x8C, 0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x5A, 0x5B]
            return push([self.dist(), self.dist()]) + bytes([rng.choice(two), 0x21])
        if c == 1:
            one = [0x64, 0x65, 0x66, 0x67, 0x5C, 0x56, 0x57]
            return push([self.dist()]) + bytes([rng.choice(one), 0x21])
        if c == 2:
            return push([1, 2, 3]) + bytes([0x8A, 0x21, 0x21, 0x21])
        if c == 3:
            return push([self.dist(), self.dist()]) + bytes([0x23, 0x21, 0x21])
        if c == 4:
            return push([self.dist(), self.dist(), self.dist(), rng.randrange(1, 4)]) + bytes([rng.choice([0x25, 0x26]), 0x21, 0x21, 0x21, 0x21])
        if c == 5:
            return push([self.dist()]) + bytes([0x20, 0x21, 0x21])
        if c == 6:
            return bytes([0x24, 0x21])
        return push([self.dist(), self.dist()]) + bytes([0x62, 0x21])  # DIV, maybe by zero

    def s_storage(self):
        rng = self.rng
        c = rng.randrange(5)
        if c == 0:
            return push([self.storage(), self.dist()]) + bytes([0x42])
        if c == 1:
            return push([self.storage()]) + bytes([0x43, 0x21])
        if c == 2:
            return push([self.cvt(), self.dist()]) + bytes([0x44])
        if c == 3:
            return push([self.cvt(), self.funits()]) + bytes([0x70])
        return push([self.cvt()]) + bytes([0x45, 0x21])

    def s_measure(self):
        rng = self.rng
        c = rng.randrange(5)
        if c == 0:
            return push([self.point()]) + bytes([rng.choice([0x46, 0x47]), 0x21])
        if c == 1:
            return push(self.points(2)) + bytes([rng.choice([0x49, 0x4A]), 0x21])
        if c == 2:
            return bytes([rng.choice([0x4B, 0x4C]), 0x21])
        if c == 3:
            return push([rng.choice([1, 2, 4, 8, 32, 64, 96, 0x1000, 0x1FFF, 0x2000])]) + bytes([0x88, 0x21])
        return bytes([rng.choice([0x4D, 0x4E])])

    def s_if(self):
        rng = self.rng
        cond = rng.choice([0, 1])
        then = self.moves(2)
        other = self.moves(2)
        if rng.random() < 0.5:
            return push([cond]) + bytes([0x58]) + then + bytes([0x1B]) + other + bytes([0x59])
        return push([cond]) + bytes([0x58]) + then + bytes([0x59])

    def s_call(self):
        rng = self.rng
        c = rng.randrange(4)
        if c == 0 and self.functions:
            return push(self.points(2) + [rng.randrange(self.functions)]) + bytes([0x2B])
        if c == 1 and self.functions:
            return push(self.points(3) + [rng.randrange(1, 4), rng.randrange(self.functions)]) + bytes([0x2A])
        if c == 2 and self.idefs:
            return push(self.points(1)) + bytes([rng.choice(self.idefs)])
        return push([rng.choice([self.functions + 3, 60, -1])]) + bytes([0x2B])

    def s_flip(self):
        rng = self.rng
        if rng.random() < 0.5:
            return push(self.points(2)) + bytes([rng.choice([0x81, 0x82])])
        return push([rng.randrange(1, 4)]) + bytes([0x17]) + push(self.points(rng.randrange(1, 4))) + bytes([0x80])

    def s_misc(self):
        return bytes([self.rng.choice([0x21, 0x22, 0x59, 0x7E, 0x7F, 0x8F, 0x28, 0x7B, 0x84, 0x90, 0x91, 0x92, 0x93, 0xAF, 0x4F, 0xB1])])

    def s_push(self):
        """The wider forms of the push instructions: eight and more values, bytes and words."""
        rng = self.rng
        n = rng.choice([8, 9, 12])
        wide = rng.random() < 0.5
        values = [rng.randrange(-300, 300) if wide else rng.randrange(0, 256) for _ in range(n)]
        return push(values) + bytes([0x21] * n)

    def s_jump(self):
        """A forward jump over a few instructions: JMPR, or JROT/JROF with a condition."""
        rng = self.rng
        skipped = self.moves(rng.randrange(1, 3))
        offset = 1 + len(skipped)
        kind = rng.choice(["jmpr", "jrot", "jrof", "jmpr0"])
        if kind == "jmpr":
            return push([offset]) + bytes([0x1C]) + skipped
        if kind == "jmpr0":
            return push([0]) + bytes([0x1C]) + skipped  # a jump to itself: rejected as a bad argument
        return push([offset, rng.choice([0, 1])]) + bytes([0x78 if kind == "jrot" else 0x79]) + skipped

    def moves(self, count):
        return b"".join(self.s_moves() for _ in range(count))

    def program(self):
        rng = self.rng
        parts = []
        # most glyph programs start the way a real one does: vectors, rounding, a reference point
        if rng.random() < 0.7:
            parts.append(bytes([rng.choice([0x00, 0x01])]))
        if rng.random() < 0.5:
            parts.append(bytes([0x18]))
        for _ in range(rng.randrange(4, 26)):
            parts.append(self.stmt())
        if rng.random() < 0.6:
            parts.append(bytes([0x30, 0x31]) if rng.random() < 0.5 else bytes([0x31, 0x30]))
        if rng.random() < 0.3:
            parts.append(self.s_moves())
        return b"".join(parts)

    def forced(self, index):
        """A statement that walks the opcodes in order, so that every one is run whatever the random choices are."""
        rng = self.rng
        walk = []
        for i in range(32):
            walk.append(lambda i=i: push([self.point()]) + bytes([0xC0 + i]))
        for i in range(32):
            walk.append(lambda i=i: push([self.point(), self.cvt()]) + bytes([0xE0 + i]))
        for opcode in range(0x68, 0x70):
            walk.append(lambda opcode=opcode: push([self.dist()]) + bytes([opcode, 0x21]))
        for opcode in (0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x0E, 0x18, 0x19, 0x3D, 0x7A, 0x7C, 0x7D, 0x30, 0x31, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F):
            walk.append(lambda opcode=opcode: bytes([opcode]))
        for opcode in (0x2E, 0x2F, 0x29, 0x10, 0x11, 0x12):
            walk.append(lambda opcode=opcode: push([self.point()]) + bytes([opcode]))
        for opcode in (0x06, 0x07, 0x08, 0x09, 0x86, 0x87, 0x27, 0x81, 0x82, 0x49, 0x4A):
            walk.append(lambda opcode=opcode: push(self.points(2)) + bytes([opcode]) + (b"\x21" if opcode in (0x49, 0x4A) else b""))
        for opcode in (0x46, 0x47):
            walk.append(lambda opcode=opcode: push([self.point()]) + bytes([opcode, 0x21]))
        walk.append(lambda: self.s_jump())
        walk.append(lambda: self.s_jump())
        walk.append(lambda: self.s_push())
        walk.append(lambda: self.s_push())
        walk.append(lambda: self.s_push())
        walk.append(lambda: push([self.zone()]) + bytes([0x16]))
        return walk[index % len(walk)]()


def build_font_program():
    """fpgm: functions 0..3 and instruction definitions, which glyph programs call."""
    out = bytearray()
    # function 0: MDAP[rnd] on the point on the stack, MDRP on another
    out += push([0]) + bytes([0x2C, 0x2F, 0xCF, 0x2D])
    # function 1: SRP1 then IP (one loop point)
    out += push([1]) + bytes([0x2C, 0x11, 0x39, 0x2D])
    # function 2: calls function 0
    out += push([2]) + bytes([0x2C]) + push([0]) + bytes([0x2B, 0x2D])
    # function 3: rounds a distance and drops it
    out += push([3]) + bytes([0x2C, 0x68, 0x21, 0x2D])
    functions = 4
    # instruction definitions for opcodes that mean nothing otherwise: MPPEM, ADD, POP
    idefs = [0x83, 0x28]
    for opcode in idefs:
        out += push([opcode]) + bytes([0x89, 0x4B, 0x60, 0x21, 0x2D])
    return bytes(out), functions, idefs


def build_cvt_program(rng):
    """prep: sets the graphics state a CVT program sets, scales a few control values, and places twilight points."""
    out = bytearray()
    out += push([64]) + bytes([0x1A]) + push([68]) + bytes([0x1D])     # SMD, SCVTCI
    out += push([rng.choice([0, 32, 40])]) + bytes([0x1E])              # SSWCI
    out += push([rng.choice([0, 64])]) + bytes([0x1F])                  # SSW
    out += push([rng.choice([1, 1, 4, 5])]) + bytes([0x85])             # SCANCTRL
    out += push([rng.choice([4, 4, 5])]) + bytes([0x8D])                # SCANTYPE
    out += push([rng.choice([9, 9, 12])]) + bytes([0x5E])               # SDB
    out += push([rng.choice([3, 3, 4])]) + bytes([0x5F])                # SDS
    # a few control values rewritten in pixels and in font units
    for _ in range(rng.randrange(2, 6)):
        out += push([rng.randrange(CVT_COUNT), rng.randrange(20, 500)]) + bytes([rng.choice([0x44, 0x70])])
    # twilight points: MIAP into zone 0
    out += push([0]) + bytes([0x16])                                    # SZPS 0: twilight
    out += bytes([0x01])                                                # SVTCA[x]
    for t in range(rng.randrange(2, 6)):
        out += push([t, rng.randrange(CVT_COUNT)]) + bytes([0x3E])      # MIAP[0]
    out += bytes([0x00])                                                # SVTCA[y]
    for t in range(rng.randrange(2, 6)):
        out += push([t, rng.randrange(CVT_COUNT)]) + bytes([0x3F])      # MIAP[1]
    out += push([1]) + bytes([0x16])                                    # SZPS 1: back to the glyph zone
    for s in range(rng.randrange(2, 6)):
        out += push([s, rng.randrange(-100, 300)]) + bytes([0x42])      # storage
    # GETINFO / MPPEM based branches, as real CVT programs have
    out += push([1]) + bytes([0x88]) + push([rng.choice([35, 40])]) + bytes([0x54, 0x58])
    out += push([rng.randrange(CVT_COUNT), rng.randrange(20, 300)]) + bytes([0x44, 0x59])
    out += bytes([0x4B]) + push([rng.choice([9, 12, 20])]) + bytes([0x52, 0x58])
    out += push([rng.randrange(CVT_COUNT), rng.randrange(20, 300)]) + bytes([0x44, 0x59])
    return bytes(out)


def glyph_outline(rng):
    """Random contours as lists of (x, y, on-curve)."""
    contours = []
    for _ in range(rng.randrange(1, 3)):
        n = rng.randrange(3, 12)
        cx, cy = rng.randrange(200, 900), rng.randrange(0, 600)
        rx, ry = rng.randrange(100, 500), rng.randrange(100, 700)
        pts = []
        for i in range(n):
            a = 2 * math.pi * i / n
            x = int(cx + rx * math.cos(a) * rng.uniform(0.7, 1.1))
            y = int(cy + ry * math.sin(a) * rng.uniform(0.7, 1.1))
            pts.append((x, y, rng.random() < 0.65))
        if not any(p[2] for p in pts):
            pts[0] = (pts[0][0], pts[0][1], True)
        contours.append(pts)
    return contours


def load_freetype(path):
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


def build_font():
    from array import array

    from fontTools.fontBuilder import FontBuilder
    from fontTools.pens.ttGlyphPen import TTGlyphPen
    from fontTools.ttLib import newTable
    from fontTools.ttLib.tables import ttProgram
    from fontTools.ttLib.tables._g_l_y_f import Glyph, GlyphComponent

    rng = random.Random(SEED)
    simple = ["s%d" % i for i in range(1, SIMPLE_GLYPHS + 1)]
    composite = ["c%d" % i for i in range(1, COMPOSITE_GLYPHS + 1)]
    names = [".notdef"] + simple + composite
    fb = FontBuilder(1000, isTTF=True)
    fb.setupGlyphOrder(names)
    fb.setupCharacterMap({0x21 + i: names[i + 1] for i in range(200)})

    fpgm_bytes, functions, idefs = build_font_program()

    glyphs = {}
    metrics = {}
    point_counts = {}
    for i, name in enumerate([".notdef"] + simple):
        contours = glyph_outline(rng)
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
        n_points = len(glyph.coordinates)
        point_counts[name] = n_points
        gen = Gen(rng, max(n_points, 1), len(contours), functions, idefs)
        prog = ttProgram.Program()
        # every opcode walked through: glyph i starts with the i-th statement of the walk
        prog.fromBytecode(gen.forced(i) + gen.program())
        glyph.program = prog
        glyphs[name] = glyph
        metrics[name] = (rng.randrange(300, 900), 0)

    # Composite glyphs: two or three components of simple glyphs (or, from the second half, of earlier composites), each with its own
    # kind of argument and transform, and a program that works on the points of the components put together.
    for k, name in enumerate(composite):
        g = Glyph()
        g.numberOfContours = -1
        comps = []
        total_points = 0
        pool = simple if k < COMPOSITE_GLYPHS // 2 else simple + composite[:k]
        for c in range(rng.randrange(1, 4)):
            comp = GlyphComponent()
            comp.glyphName = rng.choice(pool)
            pts = point_counts.get(comp.glyphName, 20)
            total_points += pts
            if c > 0 and rng.random() < 0.35 and total_points > pts:
                comp.firstPt = rng.randrange(max(total_points - pts, 1))       # a point of the components before
                comp.secondPt = rng.randrange(max(pts, 1))                    # a point of this one
                comp.flags = rng.choice([0, ROUND_XY_TO_GRID])
            else:
                comp.x = rng.choice([0, 0, rng.randrange(-120, 120), rng.randrange(-400, 400), rng.randrange(-40, 40)])
                comp.y = rng.choice([0, 0, rng.randrange(-120, 120), rng.randrange(-400, 400), rng.randrange(-40, 40)])
                comp.flags = rng.choice([0, ROUND_XY_TO_GRID, ROUND_XY_TO_GRID, SCALED_COMPONENT_OFFSET, UNSCALED_COMPONENT_OFFSET, SCALED_COMPONENT_OFFSET | ROUND_XY_TO_GRID])
            if rng.random() < 0.3:
                comp.flags |= USE_MY_METRICS
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
        gen = Gen(rng, max(total_points, 1), 3, functions, idefs)
        prog = ttProgram.Program()
        prog.fromBytecode(gen.forced(k) + gen.program() if rng.random() < 0.85 else b"")
        g.program = prog
        glyphs[name] = g
        metrics[name] = (rng.randrange(300, 900), 0)
        point_counts[name] = total_points

    fb.setupGlyf(glyphs)
    fb.setupHorizontalMetrics(metrics)
    fb.setupHorizontalHeader(ascent=800, descent=-200)
    fb.setupNameTable({"familyName": "HintingOpcodes", "styleName": "Regular"})
    fb.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
    fb.setupPost()

    font = fb.font
    fpgm = newTable("fpgm")
    fpgm.program = ttProgram.Program()
    fpgm.program.fromBytecode(fpgm_bytes)
    font["fpgm"] = fpgm

    prep = newTable("prep")
    prep.program = ttProgram.Program()
    prep.program.fromBytecode(build_cvt_program(rng))
    font["prep"] = prep

    cvt = newTable("cvt ")
    cvt.values = array("h", [rng.randrange(-40, 700) for _ in range(CVT_COUNT)])
    font["cvt "] = cvt

    maxp = font["maxp"]
    maxp.maxZones = 2
    maxp.maxTwilightPoints = TWILIGHT
    maxp.maxStorage = STORE_COUNT
    maxp.maxFunctionDefs = functions + 4
    maxp.maxInstructionDefs = len(idefs)
    maxp.maxStackElements = 96
    # head flag bit 3: force ppem to integer values (rounded as most real fonts ask for)
    font["head"].flags |= 8
    return font


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--freetype")
    parser.add_argument("--allow-other-version", action="store_true")
    args = parser.parse_args()

    freetype, raw = load_freetype(args.freetype)
    version = freetype.version()
    if version != (2, 14, 3) and not args.allow_other_version:
        sys.exit("FreeType %d.%d.%d loaded; the port derives from 2.14.3 (see --freetype)" % version)

    os.environ.setdefault("SOURCE_DATE_EPOCH", "1767225600")
    font = build_font()
    buf = io.BytesIO()
    font.save(buf)
    data = buf.getvalue()
    with open(OUT_FONT, "wb") as f:
        f.write(data)
    print("wrote", OUT_FONT, len(data), "bytes")

    glyph_count = SIMPLE_GLYPHS + COMPOSITE_GLYPHS
    cbuf = ctypes.create_string_buffer(data, len(data))
    result = {
        "freetype": {"version": "%d.%d.%d" % version, "tag": "VER-2-14-3"},
        "format": "per glyph: a=advance (26.6), e=contour end points, x/y=coordinates (26.6), t=1 for an on-curve point, err=FreeType's error",
        "modes": {},
    }

    for mode_name, (interpreter, mono, sizes) in MODES.items():
        lib = freetype.FT_Library()
        assert raw.FT_Init_FreeType(ctypes.byref(lib)) == 0
        v = ctypes.c_uint(interpreter)
        assert raw._lib.FT_Property_Set(lib, b"truetype", b"interpreter-version", ctypes.byref(v)) == 0
        face = freetype.FT_Face()
        assert raw.FT_New_Memory_Face(lib, cbuf, len(data), 0, ctypes.byref(face)) == 0
        flags = FT_LOAD_NO_BITMAP | FT_LOAD_NO_AUTOHINT | (FT_LOAD_TARGET_MONO if mono else 0)

        runs = []
        errors = 0
        for size in sizes:
            per_glyph = {}
            for gid in range(1, glyph_count + 1):
                # a fresh size for each glyph, so FreeType runs the CVT program again (see generate_hinting_golden.py)
                assert raw.FT_Set_Char_Size(face, size, size, 72, 72) == 0
                error = raw.FT_Load_Glyph(face, gid, flags)
                if error:
                    per_glyph[str(gid)] = {"err": error}
                    errors += 1
                    continue
                slot = face.contents.glyph.contents
                outline = slot.outline
                n = outline.n_points
                per_glyph[str(gid)] = {
                    "a": slot.advance.x,
                    "e": [outline.contours[i] for i in range(outline.n_contours)],
                    "x": [outline.points[i].x for i in range(n)],
                    "y": [outline.points[i].y for i in range(n)],
                    "t": [outline.tags[i] & 1 for i in range(n)],
                }
            runs.append({"size": size, "glyphs": per_glyph})
        result["modes"][mode_name] = runs
        print(mode_name, "load errors:", errors)
        raw.FT_Done_Face(face)
        raw.FT_Done_FreeType(lib)

    with gzip.GzipFile(OUT_GOLDEN, "wb", mtime=0) as f:
        f.write(json.dumps(result, separators=(",", ":")).encode("utf-8"))
    print("wrote", OUT_GOLDEN, os.path.getsize(OUT_GOLDEN), "bytes")


if __name__ == "__main__":
    main()
