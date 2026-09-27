"""Small CFF2 fonts made by hand for the hinting tests: charstrings a well-behaved font never has, and tables that are wrong in one deliberate
way (generate_hinting_cff2_fixtures.py records what FreeType 2.14.3 makes of each). Whatever it does with them (refuse the face, refuse one
glyph, draw something) the port has to do the same.

  charstring_cases()   one font whose glyphs are the cases: vsindex and blend used wrongly, stacks at their limit, operators CFF2 ignores...
  table_variants()     a font per deliberate fault of the tables: header, Top DICT, INDEXes, variation store, FDArray, FDSelect, Private DICTs
  mutants()            the baseline font with a few bytes of its CFF2 table changed at random (a fuzz whose answers FreeType gives)

The fonts are CC0 (see HintingCff2.LICENSE.txt).
"""
import importlib.util
import os
import struct

import hinting_cff2_builder as b

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("hinting_cff1", os.path.join(HERE, "generate_hinting_cff_fixtures.py"))
cff1 = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(cff1)
num, op = cff1.num, cff1.op

BL = b.blended
HSTEM, VSTEM, VMOVETO, RLINETO, HLINETO, VLINETO, RRCURVETO = 1, 3, 4, 5, 6, 7, 8
CALLSUBR, RETURN, ENDCHAR, VSINDEX, BLEND = 10, 11, 14, 15, 16
HSTEMHM, HINTMASK, CNTRMASK, RMOVETO, HMOVETO, VSTEMHM = 18, 19, 20, 21, 22, 23
CALLGSUBR = 29


def n(*values):
    """Charstring operands, some of which may be blended (`BL(default, [deltas])`)."""
    return b.operands(list(values), num, b.CHARSTRING_BLEND)


def o(code, esc=False):
    return op(code, esc)


def bias(count):
    return 107 if count < 1240 else 1131 if count < 33900 else 32768


def call_local(index, count):
    return num(index - bias(count)) + o(CALLSUBR)


def call_global(index, count):
    return num(index - bias(count)) + o(CALLGSUBR)


def sq(x=50, w=300, h=400):
    """A closed rectangle."""
    return n(x, 0) + o(RMOVETO) + n(w) + o(HLINETO) + n(h) + o(VLINETO) + n(-w) + o(HLINETO)


def blended_sq():
    return n(BL(50, [3, 4]), 0) + o(RMOVETO) + n(BL(300, [100, 50])) + o(HLINETO) + n(BL(400, [-20, 30])) + o(VLINETO) + n(BL(-300, [-100, -50])) + o(HLINETO)


def edit(data, offset, value):
    """`data` with the bytes at `offset` replaced."""
    if isinstance(value, int):
        value = bytes([value])
    return data[:offset] + value + data[offset + len(value):]


def font_of(table, count, axes=True, names=None):
    names = names or [".notdef"] + ["g%d" % i for i in range(1, count)]
    return b.build_font(table, names[:count], b.AXES if axes else None, b.AVAR if axes else None)


# ---- charstring cases ---------------------------------------------------------------------------------------------------------

def charstring_cases():
    """(name, font bytes, glyph count): one font, one glyph for each case."""
    ls = [
        n(BL(20, [3, 4])) + o(HLINETO),                                              # 0: a blend over data set 0
        num(1) + o(VSINDEX) + n(BL(10, [1, 2, 3])) + o(VLINETO),                     # 1: sets vsindex 1, then blends over it
        n(0, 20) + o(HSTEMHM),                                                       # 2: a hint
        n(30) + o(HLINETO) + o(RETURN) + n(30) + o(VLINETO),                         # 3: an explicit return (which CFF2 ignores) in the middle
        n(BL(5, [1, 1, 1])) + o(HLINETO),                                            # 4: a blend over data set 1 (three regions)
    ]
    gs = [
        n(10, 10) + o(RLINETO),                                                      # 0
        call_global(1, 3),                                                           # 1: calls itself
        n(5) + o(HLINETO),                                                           # 2
    ]
    cases = []

    def case(name, data):
        cases.append((name, data))

    case(".notdef", n(500) + o(HMOVETO))
    case("blended path", blended_sq())
    case("blended hints", n(BL(-15, [2, -1]), BL(30, [4, 2])) + o(HSTEMHM) + sq())
    case("vsindex 1 blend over three regions", num(1) + o(VSINDEX) + n(50, 0) + o(RMOVETO) + n(BL(300, [10, 20, 30])) + o(HLINETO) + n(400) + o(VLINETO))
    case("vsindex out of range", num(9) + o(VSINDEX) + blended_sq())
    case("vsindex negative", num(-1) + o(VSINDEX) + blended_sq())
    case("vsindex after blend", blended_sq() + num(1) + o(VSINDEX) + n(10) + o(HLINETO))
    case("vsindex twice", num(2) + o(VSINDEX) + num(1) + o(VSINDEX) + n(50, 0) + o(RMOVETO) + n(BL(300, [10, 20, 30])) + o(HLINETO) + n(400) + o(VLINETO))
    case("vsindex not an integer", num(1.5) + o(VSINDEX) + blended_sq())
    case("blend of 1000 values", n(50, 0) + o(RMOVETO) + num(1000) + o(BLEND) + n(20) + o(HLINETO))
    case("blend underflow", n(50, 0) + o(RMOVETO) + num(5) + num(1) + o(BLEND) + n(20) + o(HLINETO))
    case("blend of no values", n(50, 0) + o(RMOVETO) + num(0) + o(BLEND) + n(300) + o(HLINETO) + n(400) + o(VLINETO))
    case("blend after plain operands", n(50, 10, 0) + n(BL(20, [3, 4])) + o(RMOVETO) + n(100) + o(HLINETO))
    case("endchar in the middle", sq() + o(ENDCHAR) + sq(400, 100, 100))
    case("return at the top level", sq() + o(RETURN) + sq(400, 100, 100))
    case("endchar with four arguments", sq() + n(0, 0, 65, 66) + o(ENDCHAR))
    case("hstem with a width argument", n(10, 0, 20) + o(HSTEM) + sq())
    case("512 operands", n(*[i % 7 for i in range(512)]) + o(RLINETO))
    case("514 operands", n(*[i % 7 for i in range(514)]) + o(RLINETO))
    case("hintmask with no stems", o(HINTMASK) + sq())
    case("97 stems", (n(*([10, 5] * 40)) + o(HSTEMHM)) * 3 + o(HINTMASK) + b"\xff" * 13 + sq())
    case("local subroutine that blends", call_local(0, len(ls)) + n(30) + o(VLINETO))
    case("local subroutine that sets vsindex", n(50, 0) + o(RMOVETO) + call_local(1, len(ls)) + n(40) + o(HLINETO))
    case("local subroutine of another data set", num(1) + o(VSINDEX) + n(50, 0) + o(RMOVETO) + call_local(0, len(ls)) + n(40) + o(VLINETO))
    case("local subroutine for data set one", num(1) + o(VSINDEX) + n(50, 0) + o(RMOVETO) + call_local(4, len(ls)) + n(40) + o(VLINETO))
    case("subroutine with a return in the middle", n(50, 0) + o(RMOVETO) + call_local(3, len(ls)) + n(20) + o(HLINETO))
    case("global subroutines that call themselves", call_global(1, len(gs)))
    case("flex with blends", n(50, 0) + o(RMOVETO) + n(BL(10, [1, 2]), BL(20, [2, 1]), 30, 40, 50, 60, 10, 20, 30, 40, 50, 60, 50) + o(35, True))
    case("blended stems and a hintmask", n(BL(0, [3, 1]), 20, 100, 20) + o(HSTEMHM) + n(BL(30, [2, 2]), 40) + o(VSTEMHM) + o(HINTMASK) + bytes([0xE0]) + sq())
    case("operators CFF2 does not have", n(1, 2) + o(10, True) + n(3) + o(12, True) + o(23, True) + sq() + n(4) + o(0) + o(2) + o(17) + sq(400, 100, 100))
    case("numbers of every size", n(BL(1131, [1000, -32768]), BL(-1131, [32767, 5])) + o(HMOVETO) + num(70000) + o(HLINETO))
    case("fixed numbers in a blend", n(50, 0) + o(RMOVETO) + n(BL(100.5, [10.25, -3.75])) + o(HLINETO) + n(BL(-99.25, [0.5, 0.125])) + o(VLINETO))
    case("a curve of eight blends", n(50, 0) + o(RMOVETO) + n(*[BL(10 + i, [i, -i]) for i in range(6)]) + o(RRCURVETO) + n(BL(10, [2, 2])) + o(HLINETO))
    case("blends of stems, then a counter mask", n(BL(0, [1, 2]), 20, 100, 20) + o(HSTEMHM) + o(CNTRMASK) + bytes([0xC0]) + sq())
    case("stack of 513 at a blend", n(*([1] * 500)) + n(*[BL(i, [1, 2]) for i in range(4)]) + o(RLINETO))
    return _build_cases(cases, ls, gs)


def _build_cases(cases, local_subrs, global_subrs):
    vs = b.variation_store(2, b.REGIONS, b.DATA_SETS)
    private = b.private_dict([
        (b.BLUE_VALUES, b.delta_values([-15, 0, 486, BL(500, [3, -2]), 700, 715])),
        (b.STD_HW, [BL(68, [6, -3])]),
        (b.STD_VW, [84]),
    ], True)
    table = b.build_table([c for _, c in cases], global_subrs, [b.FontDict(private, local_subrs)], None, vs)
    names = [".notdef"] + ["case%d" % i for i in range(1, len(cases))]
    return b.build_font(table, names, b.AXES, b.AVAR), len(cases), [name for name, _ in cases]


# ---- the baseline and its faults --------------------------------------------------------------------------------------------------

def baseline():
    """A font with two Font DICTs (an FDSelect of format 3), a variation store, blended Private DICTs, local and global subroutines and eight
    glyphs: (table, layout, glyph count)."""
    ls0 = [n(BL(20, [3, 4])) + o(HLINETO), n(0, 20) + o(HSTEMHM)]
    ls1 = [n(BL(5, [1, 1, 1])) + o(VLINETO), n(15) + o(HLINETO)]
    gs = [n(10, 10) + o(RLINETO), n(BL(6, [1, 2])) + o(HLINETO)]
    glyphs = [
        n(500) + o(HMOVETO),
        blended_sq(),
        num(1) + o(VSINDEX) + n(50, 0) + o(RMOVETO) + n(BL(300, [10, 20, 30])) + o(HLINETO) + n(400) + o(VLINETO),
        call_local(0, 2) + sq(),
        n(50, 0) + o(RMOVETO) + n(BL(200, [5, 6, 7])) + o(HLINETO) + call_local(0, 2) + n(300) + o(VLINETO),
        n(BL(-15, [2, -1]), BL(30, [4, 2]), 100, 20) + o(HSTEMHM) + n(BL(30, [2, 2]), 40) + o(VSTEMHM) + o(HINTMASK) + bytes([0xE0]) + blended_sq(),
        call_global(1, 2) + call_global(0, 2) + sq(),
        n(50, 0) + o(RMOVETO) + n(BL(10, [1, 2]), 20, 30, 40, 50, 60, 10, 20, 30, 40, 50, 60, 50) + o(35, True),
    ]
    p0 = b.private_dict([
        (b.BLUE_VALUES, b.delta_values([-15, 0, 486, BL(500, [3, -2]), 700, 715])),
        (b.OTHER_BLUES, b.delta_values([-235, -220])),
        (b.STD_HW, [BL(68, [6, -3])]),
        (b.STD_VW, [84]),
        (b.BLUE_SCALE, [0.039625], b.dreal),
    ], True)
    p1 = b.private_dict([
        (b.VSINDEX, [1]),
        (b.BLUE_VALUES, b.delta_values([-10, 0, 470, BL(480, [2, 2, 2]), 690, 700])),
        (b.STD_HW, [50]),
        (b.STD_VW, [BL(90, [1, 2, 3])]),
    ], True)
    layout = {}
    fds = [b.FontDict(p0, ls0), b.FontDict(p1, ls1)]
    select = b.fd_select(3, [0, 0, 0, 0, 1, 1, 1, 1])
    table = b.build_table(glyphs, gs, fds, select, b.variation_store(2, b.REGIONS, b.DATA_SETS), layout=layout)
    layout["glyphs"] = glyphs
    layout["gs"] = gs
    layout["fds"] = fds
    layout["select"] = select
    return table, layout, len(glyphs)


def _shared_vstore(sets):
    """A variation store with `sets` data sets that are one and the same (every offset names it), of two regions."""
    region_list = struct.pack(">HH", 2, 1) + b"".join(struct.pack(">HHH", b.f2dot14(a), b.f2dot14(p), b.f2dot14(e)) for a, p, e in [(0, 1, 1), (0, 0, 0)])
    data_set = struct.pack(">HHH", 0, 0, 1) + struct.pack(">H", 0)
    header = 8 + 4 * sets
    body = struct.pack(">HIH", 1, header, sets) + struct.pack(">I", header + len(region_list)) * sets + region_list + data_set
    return struct.pack(">H", len(body) & 0xFFFF) + body


def _vstore_offsets(nsets):
    """Where the parts of the baseline's variation store are, from its start (the length of 2 bytes first)."""
    ivs = 2
    region_list = ivs + 8 + 4 * nsets
    return {"ivs": ivs, "format": ivs, "region_list_offset": ivs + 2, "data_count": ivs + 6, "data_offsets": ivs + 8, "region_list": region_list,
            "axis_count": region_list, "region_count": region_list + 2, "regions": region_list + 4}


def table_variants():
    """(name, font bytes, glyph count, whether the font has axes) for the deliberate faults."""
    base, layout, count = baseline()
    glyphs, gs, fds, select = layout["glyphs"], layout["gs"], layout["fds"], layout["select"]
    out = []

    def add(name, table, glyph_count=count, axes=True):
        out.append((name, font_of(table, glyph_count, axes), glyph_count, axes))

    def rebuild(**kw):
        params = dict(charstrings=glyphs, global_subrs=gs, fds=fds, select=select, vstore=b.variation_store(2, b.REGIONS, b.DATA_SETS))
        params.update(kw)
        return b.build_table(**params)

    add("baseline", base)

    # -- the header
    add("header: major version 1", edit(base, 0, 1))
    add("header: major version 3", edit(base, 0, 3))
    add("header: minor version 1", edit(base, 1, 1))
    add("header: size 4", edit(base, 2, 4))
    add("header: size 6 with a padding byte", rebuild(header_size=6))
    add("header: size 9 with padding", rebuild(header_size=9))
    add("header: size 255", edit(base, 2, 255))
    add("header: Top DICT of length 0", edit(base, 3, b"\x00\x00"))
    add("header: Top DICT one byte too long", edit(base, 3, struct.pack(">H", layout["top_length"] + 1)))
    add("header: Top DICT one byte too short", edit(base, 3, struct.pack(">H", layout["top_length"] - 1)))
    add("header: Top DICT of 65535 bytes", edit(base, 3, b"\xff\xff"))
    add("header: Top DICT of half its length", edit(base, 3, struct.pack(">H", layout["top_length"] // 2)))
    add("table of 4 bytes", base[:4])
    add("table of 5 bytes", base[:5])
    add("table cut after the Top DICT", base[:layout["global_subrs"]])
    add("table cut in the middle of the variation store", base[:layout["vstore"] + 12])
    add("table cut after the CharStrings", base[:layout["fdselect"]])
    add("table cut after the FDArray", base[:layout["private_offsets"][0]])
    add("table cut in the Private DICTs", base[:layout["private_offsets"][1] + 10])

    # -- the Top DICT
    add("Top DICT: an operator FreeType ignores", rebuild(top_extra=b.dnum(3) + b"\x0e"))
    add("Top DICT: FontBBox (not a CFF2 operator)", rebuild(top_extra=b.dnum(0) + b.dnum(-200) + b.dnum(1000) + b.dnum(900) + b.dop(5)))
    add("Top DICT: maxstack 100", rebuild(max_stack=100))
    add("Top DICT: maxstack 513", rebuild(max_stack=513))
    add("Top DICT: maxstack 10000", rebuild(max_stack=10000))
    add("Top DICT: maxstack of a real number", rebuild(top_extra=b.dreal(600.5) + b.dop(b.MAXSTACK)))
    add("Top DICT: an operator with no operand", rebuild(top_extra=b.dop(b.MAXSTACK)))
    add("Top DICT: 513 operands and an operator", rebuild(top_extra=b"".join(b.dnum(1) for _ in range(513)) + b.dop(0x0C00 | 30)))
    add("Top DICT: 514 operands", rebuild(top_extra=b"".join(b.dnum(1) for _ in range(514))))
    add("Top DICT: a font matrix", rebuild(top_extra=b"".join(b.dreal(x) for x in (0.001, 0, 0.0002, 0.001, 0, 0)) + b.dop(b.FONT_MATRIX)))
    add("Top DICT: a font matrix of 1/2000", rebuild(top_extra=b"".join(b.dreal(x) for x in (0.0005, 0, 0, 0.0005, 0, 0)) + b.dop(b.FONT_MATRIX)))
    add("Top DICT: a font matrix with too few numbers", rebuild(top_extra=b"".join(b.dreal(x) for x in (0.001, 0, 0)) + b.dop(b.FONT_MATRIX)))
    add("Top DICT: a degenerate font matrix", rebuild(top_extra=b"".join(b.dreal(x) for x in (0, 0, 0, 0, 0, 0)) + b.dop(b.FONT_MATRIX)))
    add("Top DICT: an unterminated real at the end", rebuild(top_extra=b"\x1e\x12\x3a"))
    add("Top DICT: FDSelect operator with no operand", rebuild(top_extra=b.dop(b.FD_SELECT)))
    add("Top DICT: vstore operator twice", rebuild(top_extra=b.dint5(layout["vstore"]) + b.dop(b.VSTORE)))

    # -- the INDEXes
    cs = layout["charstrings"]
    add("CharStrings: count 0", edit(base, cs, b"\x00\x00\x00\x00"))
    add("CharStrings: count 0x7FFFFFFF", edit(base, cs, b"\x7f\xff\xff\xff"))
    add("CharStrings: count 0x80000000", edit(base, cs, b"\x80\x00\x00\x00"))
    add("CharStrings: count 0xFFFFFFFF", edit(base, cs, b"\xff\xff\xff\xff"))
    add("CharStrings: count 9 (one more than there are)", edit(base, cs, b"\x00\x00\x00\x09"))
    add("CharStrings: offset size 0", edit(base, cs + 4, 0))
    add("CharStrings: offset size 5", edit(base, cs + 4, 5))
    add("CharStrings: offset size 3", edit(base, cs + 4, 3))
    add("CharStrings: the last offset is 0", edit(base, cs + 5 + 1 * 8, b"\x00\x00"))
    add("CharStrings: an offset out of order", edit(base, cs + 5 + 1 * 2, b"\x7f\xff"))
    add("CharStrings: offset size 4", rebuild(cff2_index_off_size=4))
    add("Global subrs: count 0", rebuild(global_subrs=[]))
    add("Global subrs: count 0xFFFF", edit(base, layout["global_subrs"], b"\x00\x00\xff\xff"))
    add("Global subrs: offset size 0", edit(base, layout["global_subrs"] + 4, 0))

    # -- the variation store
    vs = layout["vstore"]
    v = _vstore_offsets(len(b.DATA_SETS))
    add("vstore: none", rebuild(vstore=None))
    add("vstore: none, glyphs that blend", rebuild(vstore=None, fds=[b.FontDict(b.private_dict([(b.STD_HW, [68])]))], select=None))
    add("vstore: format 2", edit(base, vs + v["format"], b"\x00\x02"))
    add("vstore: format 0", edit(base, vs + v["format"], b"\x00\x00"))
    add("vstore: length 0", edit(base, vs, b"\x00\x00"))
    add("vstore: region list beyond the table", edit(base, vs + v["region_list_offset"], b"\xff\xff\xff\xff"))
    add("vstore: region list at the start", edit(base, vs + v["region_list_offset"], b"\x00\x00\x00\x00"))
    add("vstore: no data sets", edit(base, vs + v["data_count"], b"\x00\x00"))
    add("vstore: 65535 data sets", edit(base, vs + v["data_count"], b"\xff\xff"))
    add("vstore: 5 data sets", edit(base, vs + v["data_count"], b"\x00\x05"))
    add("vstore: one axis", edit(base, vs + v["axis_count"], b"\x00\x01"))
    add("vstore: three axes", edit(base, vs + v["axis_count"], b"\x00\x03"))
    add("vstore: no axes", edit(base, vs + v["axis_count"], b"\x00\x00"))
    add("vstore: 65535 axes", edit(base, vs + v["axis_count"], b"\xff\xff"))
    add("vstore: no regions", edit(base, vs + v["region_count"], b"\x00\x00"))
    add("vstore: 65535 regions", edit(base, vs + v["region_count"], b"\xff\xff"))
    add("vstore: a region that starts after its peak", edit(base, vs + v["regions"], struct.pack(">HHH", b.f2dot14(0.75), b.f2dot14(0.25), b.f2dot14(1))))
    add("vstore: a region that spans zero", edit(base, vs + v["regions"], struct.pack(">HHH", b.f2dot14(-0.5), b.f2dot14(0.25), b.f2dot14(1))))
    add("vstore: a region whose peak is past its end", edit(base, vs + v["regions"], struct.pack(">HHH", b.f2dot14(0), b.f2dot14(1.5), b.f2dot14(1))))
    add("vstore: a data set at offset 0", edit(base, vs + v["data_offsets"], b"\x00\x00\x00\x00"))
    add("vstore: a data set beyond the table", edit(base, vs + v["data_offsets"], b"\xff\xff\xff\x00"))
    add("vstore: two data sets at one place", edit(base, vs + v["data_offsets"] + 4, base[vs + v["data_offsets"]:vs + v["data_offsets"] + 4]))
    ds0 = vs + v["ivs"] + struct.unpack(">I", base[vs + v["data_offsets"]:vs + v["data_offsets"] + 4])[0]
    add("vstore: a data set with 65535 region indexes", edit(base, ds0 + 4, b"\xff\xff"))
    add("vstore: a data set with no regions", edit(base, ds0 + 4, b"\x00\x00"))
    add("vstore: a region index out of range", edit(base, ds0 + 6, b"\x00\x63"))
    add("vstore: a region index of 65535", edit(base, ds0 + 6, b"\xff\xff"))
    add("vstore: 300 data sets over the same bytes", rebuild(vstore=_shared_vstore(300)))
    add("vstore: 65535 data sets over the same bytes", rebuild(vstore=_shared_vstore(65535)))

    # -- the FDArray
    fa = layout["fdarray"]
    add("FDArray: at zero", rebuild(fdarray_offset_override=0))
    add("FDArray: beyond the table", rebuild(fdarray_offset_override=0x7FFFFF00))
    add("FDArray: at the start of the CharStrings", rebuild(fdarray_offset_override=cs))
    add("FDArray: count 0", edit(base, fa, b"\x00\x00\x00\x00"))
    add("FDArray: count 1 (the FDSelect names another)", edit(base, fa, b"\x00\x00\x00\x01"))
    add("FDArray: count 3 (one more than there are)", edit(base, fa, b"\x00\x00\x00\x03"))
    add("FDArray: one Font DICT and no FDSelect", rebuild(fds=[fds[0]], select=None))
    add("FDArray: two Font DICTs and no FDSelect", rebuild(select=None))
    add("FDArray: 256 Font DICTs", rebuild(fds=[fds[0]] * 256, select=b.fd_select(3, [0, 0, 0, 0, 255, 255, 255, 255])))
    add("FDArray: 257 Font DICTs", rebuild(fds=[fds[0]] * 257, select=b.fd_select(3, [0, 0, 0, 0, 255, 255, 255, 255])))
    add("FDArray: a Font DICT with no Private DICT", rebuild(fds=[fds[0], b.FontDict(no_private=True)]))
    add("FDArray: a Font DICT with a Private DICT of size 0", rebuild(fds=[fds[0], b.FontDict(b"")]))
    add("FDArray: two Font DICTs with one Private DICT", rebuild(fds=[fds[0], b.FontDict(fds[0].private, fds[0].local_subrs)]))
    add("FDArray: a Font DICT with a font matrix", rebuild(fds=[b.FontDict(fds[0].private, fds[0].local_subrs, (0.001, 0, 0.0002, 0.001, 0, 0)), fds[1]]))
    add("FDArray: a Font DICT with a font matrix of 1/2000", rebuild(fds=[b.FontDict(fds[0].private, fds[0].local_subrs, (0.0005, 0, 0, 0.0005, 0, 0)), fds[1]]))
    add("FDArray: Font DICTs with font matrices and a Top DICT with one",
        rebuild(fds=[b.FontDict(fds[0].private, fds[0].local_subrs, (0.5, 0, 0, 0.5, 0, 0)), fds[1]],
                top_extra=b"".join(b.dreal(x) for x in (0.002, 0, 0, 0.002, 0, 0)) + b.dop(b.FONT_MATRIX)))

    # -- the FDSelect
    add("FDSelect: format 0", rebuild(select=b.fd_select(0, [0, 0, 0, 0, 1, 1, 1, 1])))
    add("FDSelect: format 0, too short", rebuild(select=b"\x00\x00\x00\x01"))
    add("FDSelect: format 3", rebuild(select=b.fd_select(3, [1, 1, 0, 0, 1, 1, 0, 0])))
    add("FDSelect: format 4", rebuild(select=b.fd_select(4, [0, 0, 0, 0, 1, 1, 1, 1])))
    add("FDSelect: format 2", rebuild(select=b"\x02" + b"\x00" * 12))
    add("FDSelect: format 3 with no ranges", rebuild(select=b"\x03\x00\x00\x00\x08"))
    add("FDSelect: format 3 that ends early", rebuild(select=b"\x03\x00\x01\x00\x00\x00\x00\x04"))
    add("FDSelect: format 3 that ranges out of order", rebuild(select=b"\x03\x00\x02\x00\x04\x00\x00\x00\x00\x01\x00\x08"))
    add("FDSelect: names a Font DICT that is not there", rebuild(select=b.fd_select(3, [0, 0, 0, 0, 5, 5, 5, 5])))
    add("FDSelect: names Font DICT 255", rebuild(select=b.fd_select(0, [0, 0, 0, 0, 255, 255, 255, 255])))
    add("FDSelect: none, but named in the Top DICT at zero", rebuild(top_extra=b.dint5(0) + b.dop(b.FD_SELECT)))

    # -- the Private DICTs
    def with_private(entries, local_subrs=None, second=None):
        private = b.private_dict(entries, bool(local_subrs))
        return rebuild(fds=[b.FontDict(private, local_subrs or []), second or fds[1]])

    blend_entry = (b.STD_HW, [BL(68, [6, -3])])
    add("Private: no blends", with_private([(b.STD_HW, [68])]))
    add("Private: vsindex 1", with_private([(b.VSINDEX, [1]), (b.STD_HW, [BL(68, [1, 2, 3])])]))
    add("Private: vsindex out of range, no blends", with_private([(b.VSINDEX, [9]), (b.STD_HW, [68])]))
    add("Private: vsindex out of range, a blend", with_private([(b.VSINDEX, [9]), blend_entry]))
    add("Private: vsindex negative", with_private([(b.VSINDEX, [-1]), blend_entry]))
    add("Private: vsindex a real number", with_private([(b.VSINDEX, [1.5], b.dreal), blend_entry]))
    add("Private: vsindex after a blend", with_private([blend_entry, (b.VSINDEX, [1])]))
    add("Private: vsindex with no operand", rebuild(fds=[b.FontDict(b.dop(b.VSINDEX) + b.dint5(10) + b.dop(b.STD_HW)), fds[1]]))
    add("Private: a blend for the wrong number of regions", with_private([(b.VSINDEX, [1]), (b.STD_HW, [BL(68, [1, 2])])]))
    add("Private: a blend that lacks its deltas", rebuild(fds=[b.FontDict(b.dnum(68) + b.dnum(1) + b.dop(b.BLEND) + b.dop(b.STD_HW)), fds[1]]))
    add("Private: a blend of no values", rebuild(fds=[b.FontDict(b.dnum(0) + b.dop(b.BLEND) + b.dnum(68) + b.dop(b.STD_HW)), fds[1]]))
    add("Private: a blend with no operand", rebuild(fds=[b.FontDict(b.dop(b.BLEND) + b.dnum(68) + b.dop(b.STD_HW)), fds[1]]))
    add("Private: a blend of 600 values", rebuild(fds=[b.FontDict(b.dnum(600) + b.dop(b.BLEND) + b.dnum(68) + b.dop(b.STD_HW)), fds[1]]))
    add("Private: two blends", with_private([(b.BLUE_VALUES, [BL(-15, [1, 2]), BL(0, [3, 4]), 400, BL(10, [5, 6])]), (b.STD_HW, [BL(68, [6, -3])])]))
    add("Private: blended real numbers", with_private([(b.BLUE_SCALE, [BL(0.04, [0.001, -0.002])], b.dreal), (b.STD_HW, [68])]))
    add("Private: BlueValues of 15 entries", with_private([(b.BLUE_VALUES, [BL(1, [1, 1])] + list(range(2, 16)))]))
    add("Private: BlueValues with an odd count", with_private([(b.BLUE_VALUES, [-15, 0, 486])]))
    add("Private: LanguageGroup 1 and no blues", with_private([(b.LANGUAGE_GROUP, [1]), (b.STD_HW, [40]), (b.STD_VW, [100])]))
    add("Private: an operator CFF2 does not have (defaultWidthX)", with_private([(20, [500]), (21, [560]), (b.STD_HW, [68])]))
    add("Private: an operator CFF2 does not have (ForceBold with no operand)", with_private([(0x0C0E, []), (b.STD_HW, [68])]))
    add("Private: ExpansionFactor with no operand", with_private([(b.EXPANSION_FACTOR, []), (b.STD_HW, [68])]))
    add("Private: StemSnapH with no operand", with_private([(b.STEM_SNAP_H, []), (b.STD_HW, [68])]))
    add("Private: 514 operands", rebuild(fds=[b.FontDict(b"".join(b.dnum(1) for _ in range(514)) + b.dop(b.BLUE_VALUES)), fds[1]]))
    add("Private: 513 operands", rebuild(fds=[b.FontDict(b"".join(b.dnum(1) for _ in range(513)) + b.dop(b.STD_HW)), fds[1]]))
    add("Private: an unterminated real", rebuild(fds=[b.FontDict(b.dnum(3) + b"\x1e\x12"), fds[1]]))
    add("Private: no local subrs but a call", with_private([blend_entry], None))
    fd_ls = fds[0].local_subrs
    p_ok = b.private_dict([blend_entry], True)
    for label, offset in (("negative", -20), ("zero", 0), ("beyond the table", 0x7FFFFF00), ("into the Private DICT", 3)):
        body = b.private_dict([blend_entry], False) + b.dint5(offset) + b.dop(b.SUBRS)
        add("Private: Subrs offset %s" % label, rebuild(fds=[b.FontDict(body, fd_ls), fds[1]]))
    add("Private: Subrs at the end of the table", rebuild(fds=[b.FontDict(p_ok, []), fds[1]]))

    # the Font DICT's Private operator, changed in the table
    fd0 = layout["fdarray"]
    fdarray_bytes = base[fd0:layout["private_offsets"][0]]
    at = 5 + 3 * 1  # the first Font DICT: its Private DICT's size is the first number (an INDEX of two entries with one-byte offsets)
    assert fdarray_bytes[at] == 0x1D
    add("Private: size 0 in the Font DICT", edit(base, fd0 + at + 1, struct.pack(">i", 0)))
    add("Private: size beyond the table", edit(base, fd0 + at + 1, struct.pack(">i", 0x7FFFFF00)))
    add("Private: size negative", edit(base, fd0 + at + 1, struct.pack(">i", -5)))
    add("Private: offset beyond the table", edit(base, fd0 + at + 6, struct.pack(">i", 0x7FFFFF00)))
    add("Private: offset negative", edit(base, fd0 + at + 6, struct.pack(">i", -5)))
    add("Private: offset zero", edit(base, fd0 + at + 6, struct.pack(">i", 0)))
    add("Private: size one more than there is", edit(base, fd0 + at + 1, struct.pack(">i", len(fds[0].private) + 1)))
    add("Private: size one less than there is", edit(base, fd0 + at + 1, struct.pack(">i", len(fds[0].private) - 1)))

    # -- glyph counts
    add("no glyphs", rebuild(charstrings=[]), 1)
    add("one glyph", rebuild(charstrings=[glyphs[0]], select=b.fd_select(0, [0])), 1)
    return out


def mutants(rng, count, table=None):
    """The table (the baseline's unless one is given) and `count` lists of edits of it, each a few random bytes changed: (offset, value)."""
    if table is None:
        table = baseline()[0]
    result = []
    length = len(table)
    for _ in range(count):
        edits = []
        for _ in range(rng.choice([1, 1, 1, 2, 2, 3, 4])):
            offset = rng.randrange(length)
            kind = rng.random()
            if kind < 0.3:
                value = table[offset] ^ (1 << rng.randrange(8))
            elif kind < 0.5:
                value = rng.choice([0, 0xFF, 0x7F, 0x80, 1, 2])
            elif kind < 0.6:
                value = (table[offset] + rng.choice([-1, 1])) & 0xFF
            else:
                value = rng.randrange(256)
            edits.append((offset, value))
        result.append(edits)
    return table, result
