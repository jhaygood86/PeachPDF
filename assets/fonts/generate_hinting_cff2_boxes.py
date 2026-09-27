#!/usr/bin/env python3
"""Generates HintingCff2Boxes.otf: a small variable font with CFF2 outlines whose letters are built of rectangles and are hinted, for the
showcase of grid-fitted CFF2 text (a font with real hinted CFF2 outlines is not bundled, and the random fixtures of the hinting tests are not
legible).

One axis, `wght` (300, 400, 900): the strokes of the letters get thicker inward, so the letters keep their widths (and the advances need no
HVAR table). A stroke is 56 units at 300, 80 at 400 and 200 at 900: the default and a delta for each of the two regions of the axis, blended by
the charstrings, whose hints (`hstemhm` for the bars, `vstemhm` for the stems, one `hintmask` for all of them) are blended too, and by the
Private DICT (blue zones for the baseline, the x-height and the cap height; StdHW and StdVW that vary with the weight).

The letters are H E F I L T O U and h i l n o u; the font is CC0 (see HintingCff2.LICENSE.txt). Requires fontTools (hinting_cff2_builder.py
does the rest).

  python generate_hinting_cff2_boxes.py
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import hinting_cff2_builder as b  # noqa: E402
import importlib.util  # noqa: E402

_spec = importlib.util.spec_from_file_location("hinting_cff1", os.path.join(HERE, "generate_hinting_cff_fixtures.py"))
cff1 = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(cff1)
num, op = cff1.num, cff1.op

OUT = os.path.join(HERE, "HintingCff2Boxes.otf")

CAP, XHEIGHT = 700, 500
AXES = [("wght", 300, 400, 900)]
# two regions of the one axis: towards 900 and towards 300
REGIONS = [[(0, 1, 1)], [(-1, -1, 0)]]
DATA_SETS = [[0, 1]]


class V:
    """A value of the design that is linear in the weight: its default and the change at each end of the axis."""

    def __init__(self, base, up=0, down=0):
        self.base, self.up, self.down = base, up, down

    def __add__(self, other):
        o = other if isinstance(other, V) else V(other)
        return V(self.base + o.base, self.up + o.up, self.down + o.down)

    __radd__ = __add__

    def __sub__(self, other):
        o = other if isinstance(other, V) else V(other)
        return V(self.base - o.base, self.up - o.up, self.down - o.down)

    def __rsub__(self, other):
        return V(other) - self

    def __neg__(self):
        return V(-self.base, -self.up, -self.down)

    def __mul__(self, k):
        return V(self.base * k, self.up * k, self.down * k)

    __rmul__ = __mul__

    def operand(self):
        if self.up == 0 and self.down == 0:
            return int(self.base) if self.base == int(self.base) else self.base
        return b.blended(self.base, [self.up, self.down])


# the stroke: 80 at the default weight, 200 at the top of the axis, 56 at the bottom
S = V(80, 120, -24)


def rect(x0, y0, x1, y1):
    """(x0, y0, width, height) of a rectangle; the values may vary with the weight."""
    return (x0, y0, x1 - x0, y1 - y0)


class Glyph:
    def __init__(self, width, rects, hstems, vstems):
        self.width, self.rects, self.hstems, self.vstems = width, rects, hstems, vstems

    def charstring(self):
        out = b""
        # horizontal stems: (edge, width) pairs, the edge relative to the top of the one before
        stems = sorted(self.hstems, key=lambda h: h[0].base)
        prev = V(0)
        operands = []
        for y, w in stems:
            operands += [(y - prev).operand(), w.operand()]
            prev = y + w
        if operands:
            out += b.operands(operands, num, b.CHARSTRING_BLEND) + op(cff1.HSTEMHM)
        stems_v = sorted(self.vstems, key=lambda v: v[0].base)
        prev = V(0)
        operands = []
        for x, w in stems_v:
            operands += [(x - prev).operand(), w.operand()]
            prev = x + w
        if operands:
            out += b.operands(operands, num, b.CHARSTRING_BLEND) + op(cff1.VSTEMHM)
        count = len(stems) + len(stems_v)
        if count:
            mask = bytearray((count + 7) // 8)
            for i in range(count):
                mask[i // 8] |= 0x80 >> (i % 8)
            out += op(cff1.HINTMASK) + bytes(mask)

        cx, cy = V(0), V(0)
        for kind, (x, y, w, h) in self.rects:
            out += b.operands([(x - cx).operand(), (y - cy).operand()], num, b.CHARSTRING_BLEND) + op(cff1.RMOVETO)
            if kind == "hole":
                # a counter runs the other way round: up first, then across
                out += b.operands([h.operand()], num, b.CHARSTRING_BLEND) + op(cff1.VLINETO) + b.operands([w.operand()], num, b.CHARSTRING_BLEND) + op(cff1.HLINETO) + b.operands([(-h).operand()], num, b.CHARSTRING_BLEND) + op(cff1.VLINETO)
                cx, cy = x + w, y
            else:
                out += b.operands([w.operand()], num, b.CHARSTRING_BLEND) + op(cff1.HLINETO) + b.operands([h.operand()], num, b.CHARSTRING_BLEND) + op(cff1.VLINETO) + b.operands([(-w).operand()], num, b.CHARSTRING_BLEND) + op(cff1.HLINETO)
                cx, cy = x, y + h
        return out


def stem_x(x0):
    return (V(x0), S)


def letters():
    """name -> Glyph."""
    g = {}
    top = V(CAP)
    mid = lambda: V(CAP // 2) - S * 0.5  # noqa: E731  the bottom of a bar centred on half the cap height
    xtop = V(XHEIGHT)

    # I: one stem
    g["I"] = Glyph(300, [("r", rect(V(0), V(0), S, top))], [(V(0), S), (V(CAP) - S, S)], [stem_x(0)])
    # L: stem and foot
    W = 420
    g["L"] = Glyph(520, [("r", rect(V(0), V(0), S, top)), ("r", rect(S, V(0), V(W), S))], [(V(0), S)], [stem_x(0)])
    # T: top bar and a centred stem
    Wt = 500
    stem0 = V(Wt / 2) - S * 0.5
    g["T"] = Glyph(600, [("r", rect(V(0), top - S, V(Wt), top)), ("r", rect(stem0, V(0), stem0 + S, top - S))],
                   [(V(CAP) - S, S)], [(stem0, S)])
    # H: two stems and a bar
    Wh = 520
    g["H"] = Glyph(620, [("r", rect(V(0), V(0), S, top)), ("r", rect(V(Wh) - S, V(0), V(Wh), top)), ("r", rect(S, mid(), V(Wh) - S, mid() + S))],
                   [(V(0), S), (mid(), S), (V(CAP) - S, S)], [stem_x(0), (V(Wh) - S, S)])
    # E: stem and three bars
    We = 460
    g["E"] = Glyph(560, [("r", rect(V(0), V(0), S, top)), ("r", rect(S, V(0), V(We), S)), ("r", rect(S, mid(), V(We - 60), mid() + S)),
                         ("r", rect(S, top - S, V(We), top))],
                   [(V(0), S), (mid(), S), (V(CAP) - S, S)], [stem_x(0)])
    # F: stem and two bars
    g["F"] = Glyph(540, [("r", rect(V(0), V(0), S, top)), ("r", rect(S, mid(), V(We - 60), mid() + S)), ("r", rect(S, top - S, V(We), top))],
                   [(mid(), S), (V(CAP) - S, S)], [stem_x(0)])
    # O: a ring
    Wo = 500
    g["O"] = Glyph(600, [("r", rect(V(0), V(0), V(Wo), top)), ("hole", rect(S, S, V(Wo) - S, top - S))],
                   [(V(0), S), (V(CAP) - S, S)], [stem_x(0), (V(Wo) - S, S)])
    # U: two stems and a foot
    Wu = 500
    g["U"] = Glyph(600, [("r", rect(V(0), V(0), S, top)), ("r", rect(V(Wu) - S, V(0), V(Wu), top)), ("r", rect(S, V(0), V(Wu) - S, S))],
                   [(V(0), S), (V(CAP) - S, S)], [stem_x(0), (V(Wu) - S, S)])
    # i and l
    g["l"] = Glyph(300, [("r", rect(V(0), V(0), S, top))], [(V(0), S), (V(CAP) - S, S)], [stem_x(0)])
    g["i"] = Glyph(300, [("r", rect(V(0), V(0), S, xtop)), ("r", rect(V(0), V(600), S, top))], [(V(0), S), (V(XHEIGHT) - S, S), (V(600), S)], [stem_x(0)])
    # h, n, u, o
    Wn = 400
    g["h"] = Glyph(500, [("r", rect(V(0), V(0), S, top)), ("r", rect(V(Wn) - S, V(0), V(Wn), xtop)), ("r", rect(S, xtop - S, V(Wn) - S, xtop))],
                   [(V(0), S), (V(XHEIGHT) - S, S)], [stem_x(0), (V(Wn) - S, S)])
    g["n"] = Glyph(500, [("r", rect(V(0), V(0), S, xtop)), ("r", rect(V(Wn) - S, V(0), V(Wn), xtop)), ("r", rect(S, xtop - S, V(Wn) - S, xtop))],
                   [(V(0), S), (V(XHEIGHT) - S, S)], [stem_x(0), (V(Wn) - S, S)])
    g["u"] = Glyph(500, [("r", rect(V(0), V(0), S, xtop)), ("r", rect(V(Wn) - S, V(0), V(Wn), xtop)), ("r", rect(S, V(0), V(Wn) - S, S))],
                   [(V(0), S), (V(XHEIGHT) - S, S)], [stem_x(0), (V(Wn) - S, S)])
    g["o"] = Glyph(500, [("r", rect(V(0), V(0), V(Wn), xtop)), ("hole", rect(S, S, V(Wn) - S, xtop - S))],
                   [(V(0), S), (V(XHEIGHT) - S, S)], [stem_x(0), (V(Wn) - S, S)])
    return g


def main():
    glyphs = letters()
    names = [".notdef", "space"] + sorted(glyphs)
    charstrings = [num(500) + op(cff1.HMOVETO), b""]  # .notdef and space draw nothing (the width is not in a CFF2 charstring)
    charstrings[0] = num(0) + num(0) + op(cff1.RMOVETO)
    advances = {".notdef": 500, "space": 280}
    for name in names[2:]:
        charstrings.append(glyphs[name].charstring())
        advances[name] = glyphs[name].width

    # blue zones: the baseline, the x-height and the cap height, with the overshoot the letters do not have; the stem widths vary with the weight
    entries = [
        (b.BLUE_VALUES, b.delta_values([-10, 0, XHEIGHT, XHEIGHT + 10, CAP, CAP + 10])),
        (b.STD_HW, [b.blended(80, [120, -24])]),
        (b.STD_VW, [b.blended(80, [120, -24])]),
        (b.BLUE_SCALE, [0.039625], b.dreal),
        (b.BLUE_SHIFT, [7]),
        (b.BLUE_FUZZ, [1]),
    ]
    private = b.private_dict(entries, False)
    table = b.build_table(charstrings, [], [b.FontDict(private)], None, b.variation_store(1, REGIONS, DATA_SETS))
    cmap = {ord(" "): "space"}
    cmap.update({ord(n): n for n in glyphs})
    font = b.build_font(table, names, AXES, None, advances=advances, cmap=cmap, style="Regular", family="HintingCff2Boxes")
    with open(OUT, "wb") as f:
        f.write(font)
    print("wrote", OUT, len(font), "bytes;", len(names), "glyphs")


if __name__ == "__main__":
    main()
