"""Builds CFF2 (variable CFF) tables and fonts by hand, for the fixtures of the CFF2 hinting tests (generate_hinting_cff2_fixtures.py).

fontTools writes CFF2 well, but it never writes what these fixtures are for: a Private DICT whose values are blended, a charstring that
blends the operands of its hints, tables that are wrong in one deliberate way. Everything here is written byte by byte, so a fixture says
exactly what it holds. The wrapper around the table (head, hhea, maxp, hmtx, cmap, name, post, fvar, avar) is fontTools'.

The fonts are CC0: they contain no third-party data (see HintingCff2.LICENSE.txt).
"""
import io
import struct

from fontTools.fontBuilder import FontBuilder
from fontTools.ttLib import newTable
from fontTools.ttLib.sfnt import SFNTReader, SFNTWriter

ESCAPE = 12


def f2dot14(value):
    return int(round(value * 16384)) & 0xFFFF


# ---- DICT data ---------------------------------------------------------------------------------------------------------------

def dnum(v):
    """A DICT integer, in the smallest of the forms."""
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
        return bytes([28]) + struct.pack(">h", v)
    return bytes([29]) + struct.pack(">i", v)


def dint5(v):
    """A DICT integer that always takes five bytes (for offsets, whose size must not depend on their value)."""
    return bytes([29]) + struct.pack(">i", v)


def dreal(x):
    """A DICT real number (binary-coded decimal)."""
    s = repr(float(x))
    if s.endswith(".0"):
        s = s[:-2]
    nibbles = []
    i = 0
    while i < len(s):
        c = s[i]
        if c.isdigit():
            nibbles.append(int(c))
        elif c == ".":
            nibbles.append(0xA)
        elif c == "-":
            nibbles.append(0xE)
        elif c in "eE":
            # "E-" is a nibble of its own; "E" alone (a positive exponent, which repr writes as e+NN) is 0xB
            if s[i + 1:i + 2] == "-":
                nibbles.append(0xC)
                i += 1
            else:
                nibbles.append(0xB)
                if s[i + 1:i + 2] == "+":
                    i += 1
        else:
            raise ValueError(s)
        i += 1
    nibbles.append(0xF)
    if len(nibbles) % 2:
        nibbles.append(0xF)
    return bytes([30]) + bytes((nibbles[i] << 4) | nibbles[i + 1] for i in range(0, len(nibbles), 2))


def dop(code):
    """A DICT operator; codes of two bytes are written as 0x0Cxx."""
    return bytes([ESCAPE, code & 0xFF]) if code > 0xFF else bytes([code])


# operators
CHARSTRINGS, PRIVATE, VSINDEX, BLEND, VSTORE, MAXSTACK, SUBRS = 17, 18, 22, 23, 24, 25, 19
FONT_MATRIX, FD_ARRAY, FD_SELECT = 0x0C07, 0x0C24, 0x0C25
BLUE_VALUES, OTHER_BLUES, FAMILY_BLUES, FAMILY_OTHER_BLUES = 6, 7, 8, 9
STD_HW, STD_VW = 10, 11
BLUE_SCALE, BLUE_SHIFT, BLUE_FUZZ, STEM_SNAP_H, STEM_SNAP_V, LANGUAGE_GROUP, EXPANSION_FACTOR = 0x0C09, 0x0C0A, 0x0C0B, 0x0C0C, 0x0C0D, 0x0C11, 0x0C12


def blended(base, deltas):
    """A value of a DICT or charstring that varies: its default and one delta for every region of the data set in use."""
    return ("blend", base, list(deltas))


# the blend operator: 23 in a DICT and 16 in a charstring
CHARSTRING_BLEND = 16


def operands(values, encode, blend_op=BLEND):
    """The operands of an operator, some of which may be blended (`blended(...)`): plain values first as they are, then the run of blended
    ones as `bases..., deltas..., n, blend`. A blended value in the middle of the plain ones is written as its own blend. `blend_op` is the
    operator of a DICT (23) or, for a charstring, CHARSTRING_BLEND."""
    out = b""
    i = 0
    while i < len(values):
        v = values[i]
        if isinstance(v, tuple) and v and v[0] == "blend":
            run = []
            while i < len(values) and isinstance(values[i], tuple) and values[i][0] == "blend":
                run.append(values[i])
                i += 1
            out += b"".join(encode(b[1]) for b in run)
            for b in run:
                out += b"".join(encode(d) for d in b[2])
            out += dnum(len(run)) + dop(blend_op)
        else:
            out += encode(v)
            i += 1
    return out


def dict_entry(op, values, encode=None):
    return operands(values, encode or dnum) + dop(op)


def delta_values(absolute):
    """The delta encoding of an array of DICT values (BlueValues and the like): each is written as the difference from the one before. A
    blended value's delta is blended too: its default is the difference of the defaults and its deltas those of the deltas."""
    out = []
    prev = None
    for v in absolute:
        if isinstance(v, tuple):
            if prev is None:
                out.append(v)
                prev = v
            else:
                p = prev if isinstance(prev, tuple) else ("blend", prev, [0] * len(v[2]))
                out.append(blended(v[1] - p[1], [a - b for a, b in zip(v[2], p[2])]))
                prev = v
        else:
            if isinstance(prev, tuple):
                out.append(v - prev[1])
            else:
                out.append(v if prev is None else v - prev)
            prev = v
    return out


# ---- INDEX, ItemVariationStore ------------------------------------------------------------------------------------------------

def index(items, cff2=True, off_size=None):
    """A CFF2 INDEX (a 32-bit count) or, with cff2=False, a CFF one (a 16-bit count)."""
    head = ">IB" if cff2 else ">HB"
    if not items:
        return struct.pack(">I" if cff2 else ">H", 0)
    offsets = [1]
    for item in items:
        offsets.append(offsets[-1] + len(item))
    if off_size is None:
        off_size = 1 if offsets[-1] < 0x100 else 2 if offsets[-1] < 0x10000 else 3 if offsets[-1] < 0x1000000 else 4
    packed = b"".join(o.to_bytes(off_size, "big") for o in offsets)
    return struct.pack(head, len(items), off_size) + packed + b"".join(items)


def variation_store(axis_count, regions, data_sets):
    """The VariationStore of a Top DICT: a 16-bit length and an ItemVariationStore with no items, only the regions each data set names.
    `regions` is a list of regions, each a list of (start, peak, end) for every axis; `data_sets` a list of lists of region indexes."""
    region_list = struct.pack(">HH", axis_count, len(regions))
    for region in regions:
        assert len(region) == axis_count
        for start, peak, end in region:
            region_list += struct.pack(">HHH", f2dot14(start), f2dot14(peak), f2dot14(end))

    datas = []
    for indexes in data_sets:
        datas.append(struct.pack(">HHH", 0, 0, len(indexes)) + b"".join(struct.pack(">H", i) for i in indexes))

    header_size = 8 + 4 * len(data_sets)
    region_list_offset = header_size
    out = struct.pack(">HIH", 1, region_list_offset, len(data_sets))
    offset = header_size + len(region_list)
    offsets = []
    for d in datas:
        offsets.append(offset)
        offset += len(d)
    out += b"".join(struct.pack(">I", o) for o in offsets) + region_list + b"".join(datas)
    return struct.pack(">H", len(out)) + out


def fd_select(kind, fds):
    """An FDSelect for `fds`, the Font DICT of every glyph: format 0, or 3 (ranges), or 4 (which FreeType does not read)."""
    if kind == 0:
        return b"\0" + bytes(fds)
    ranges = []
    for gid, fd in enumerate(fds):
        if not ranges or ranges[-1][1] != fd:
            ranges.append((gid, fd))
    if kind == 3:
        return b"\3" + struct.pack(">H", len(ranges)) + b"".join(struct.pack(">HB", g, f) for g, f in ranges) + struct.pack(">H", len(fds))
    if kind == 4:
        return b"\4" + struct.pack(">I", len(ranges)) + b"".join(struct.pack(">IH", g, f) for g, f in ranges) + struct.pack(">I", len(fds))
    raise ValueError(kind)


# ---- the table ----------------------------------------------------------------------------------------------------------------

class FontDict:
    """A Font DICT: its Private DICT (as bytes, already encoded), its local subroutines and, if it has one, a font matrix."""

    def __init__(self, private=b"", local_subrs=None, font_matrix=None, no_private=False):
        self.private = private
        self.local_subrs = local_subrs or []
        self.font_matrix = font_matrix
        self.no_private = no_private


def private_dict(entries, local_subrs_present=False):
    """The bytes of a Private DICT from (operator, [values]) entries, `Subrs` (the size of the DICT) appended when there are local
    subroutines. The sizes of the numbers do not depend on a value, so the offset of the subroutines is written in five bytes."""
    body = b"".join(dict_entry(op, values, encode) for op, values, encode in ((e[0], e[1], e[2] if len(e) > 2 else None) for e in entries))
    if local_subrs_present:
        body += dint5(len(body) + 6) + dop(SUBRS)
        assert len(dint5(0) + dop(SUBRS)) == 6
    return body


def build_table(charstrings, global_subrs=(), fds=None, select=None, vstore=None, top_extra=b"", max_stack=None, header_minor=0,
                header_size=5, top_length_delta=0, fdarray_offset_override=None, vstore_offset_override=None, cff2_index_off_size=None,
                layout=None):
    """A CFF2 table.

    charstrings   list of the glyphs' charstring bytes
    global_subrs  list of subroutine bytes
    fds           list of FontDict (at least one unless the table is meant to lack the FDArray)
    select        FDSelect bytes, or None
    vstore        VariationStore bytes (variation_store(...)), or None
    top_extra     more operators for the Top DICT, before the standard ones
    """
    fds = fds if fds is not None else [FontDict()]

    def top(charstrings_offset, fdarray_offset, fdselect_offset, vstore_offset):
        d = top_extra
        if max_stack is not None:
            d += dnum(max_stack) + dop(MAXSTACK)
        d += dint5(charstrings_offset) + dop(CHARSTRINGS)
        if vstore is not None:
            d += dint5(vstore_offset) + dop(VSTORE)
        d += dint5(fdarray_offset) + dop(FD_ARRAY)
        if select is not None:
            d += dint5(fdselect_offset) + dop(FD_SELECT)
        return d

    def font_dict(private_size, private_offset, fd):
        d = b""
        if fd.font_matrix is not None:
            d += b"".join(dreal(x) for x in fd.font_matrix) + dop(FONT_MATRIX)
        if not fd.no_private:
            d += dint5(private_size) + dint5(private_offset) + dop(PRIVATE)
        return d

    top_len = len(top(0, 0, 0, 0))
    gsubrs = index(list(global_subrs), True, cff2_index_off_size)
    vstore_bytes = vstore or b""
    charstrings_bytes = index(list(charstrings), True, cff2_index_off_size)
    select_bytes = select or b""
    fdarray_size = len(index([font_dict(0, 0, fd) for fd in fds], True, cff2_index_off_size))

    header = struct.pack(">BBBH", 2, header_minor, header_size, top_len + top_length_delta) + b"\0" * (header_size - 5)
    pos = len(header) + top_len
    pos += len(gsubrs)
    vstore_offset = pos
    pos += len(vstore_bytes)
    charstrings_offset = pos
    pos += len(charstrings_bytes)
    fdselect_offset = pos
    pos += len(select_bytes)
    fdarray_offset = pos
    pos += fdarray_size

    privates = b""
    fd_entries = []
    for fd in fds:
        size = len(fd.private)
        subrs = index(fd.local_subrs, True, cff2_index_off_size) if fd.local_subrs else b""
        fd_entries.append(font_dict(size, pos + len(privates), fd))
        privates += fd.private + subrs

    fdarray = index(fd_entries, True, cff2_index_off_size)
    assert len(fdarray) == fdarray_size
    if layout is not None:
        layout.update(top=len(header), top_length=top_len, global_subrs=len(header) + top_len, vstore=vstore_offset,
                      charstrings=charstrings_offset, fdselect=fdselect_offset, fdarray=fdarray_offset)
        layout["private_offsets"] = []
        p = fdarray_offset + fdarray_size
        for fd in fds:
            layout["private_offsets"].append(p)
            p += len(fd.private) + (len(index(fd.local_subrs, True, cff2_index_off_size)) if fd.local_subrs else 0)
    topdict = top(charstrings_offset, fdarray_offset if fdarray_offset_override is None else fdarray_offset_override, fdselect_offset,
                  vstore_offset if vstore_offset_override is None else vstore_offset_override)
    assert len(topdict) == top_len
    return header + topdict + gsubrs + vstore_bytes + charstrings_bytes + select_bytes + fdarray + privates


# ---- the font -----------------------------------------------------------------------------------------------------------------

def build_font(cff2_table, glyph_names, axes=None, avar=None, upem=1000, advances=None, cmap=None, style="Regular", family="HintingCff2"):
    """An OpenType font whose outlines are `cff2_table`, with fvar (and avar) for `axes`: a list of (tag, minimum, default, maximum)."""
    fb = FontBuilder(upem, isTTF=False)
    fb.font.sfntVersion = "OTTO"
    fb.font.recalcBBoxes = False
    fb.font.recalcTimestamp = False
    fb.setupGlyphOrder(glyph_names)
    fb.setupCharacterMap(cmap if cmap is not None else {0x20 + i: glyph_names[i] for i in range(1, min(len(glyph_names), 95))})
    advances = advances or {}
    fb.setupHorizontalMetrics({n: (advances.get(n, 500), 0) for n in glyph_names})
    fb.setupHorizontalHeader(ascent=800, descent=-200)
    fb.setupNameTable({"familyName": family, "styleName": style})
    fb.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
    fb.setupPost()
    # a fixed date, so that a font is the same bytes every time it is made
    fb.font['head'].created = fb.font['head'].modified = 3786825600
    if axes:
        fb.setupFvar([(tag, mn, df, mx, tag) for tag, mn, df, mx in axes], [])
        if avar:
            table = newTable("avar")
            table.segments = avar
            fb.font["avar"] = table

    buf = io.BytesIO()
    fb.font.save(buf)
    buf.seek(0)
    reader = SFNTReader(buf)
    tables = {tag: reader[tag] for tag in reader.tables if tag != "CFF "}
    tables["CFF2"] = cff2_table
    out = io.BytesIO()
    writer = SFNTWriter(out, len(tables), reader.sfntVersion)
    for tag in sorted(tables):
        writer[tag] = tables[tag]
    writer.close()
    return out.getvalue()


def replace_table(font_bytes, tag, table):
    """The font with one table replaced (or added)."""
    reader = SFNTReader(io.BytesIO(font_bytes))
    tables = {t: reader[t] for t in reader.tables}
    tables[tag] = table
    out = io.BytesIO()
    writer = SFNTWriter(out, len(tables), reader.sfntVersion)
    for t in sorted(tables):
        writer[t] = tables[t]
    writer.close()
    return out.getvalue()


def table_of(font_bytes, tag):
    reader = SFNTReader(io.BytesIO(font_bytes))
    return reader[tag]


# ---- the design space the fixtures share ----------------------------------------------------------------------------------------

AXES = [("wght", 200, 400, 800), ("wdth", 75, 100, 150)]
# a non-linear avar map for the weight: 0.5 -> 0.75 and -0.5 -> -0.25 (exact in 2.14 and in 16.16)
AVAR = {"wght": {-1.0: -1.0, -0.5: -0.25, 0.0: 0.0, 0.5: 0.75, 1.0: 1.0}, "wdth": {-1.0: -1.0, 0.0: 0.0, 1.0: 1.0}}

# six regions over the two axes (start, peak, end for each): weight up, weight down, width up, width down, both up, weight up to half
REGIONS = [
    [(0, 1, 1), (0, 0, 0)],
    [(-1, -1, 0), (0, 0, 0)],
    [(0, 0, 0), (0, 1, 1)],
    [(0, 0, 0), (-1, -1, 0)],
    [(0, 0.5, 1), (0, 1, 1)],
    [(0, 0.5, 1), (0, 0, 0)],
]
# four data sets, of two, three, four and one regions
DATA_SETS = [[0, 1], [2, 3, 4], [0, 2, 4, 5], [1]]


def table_offset(font_bytes, tag):
    """Where a table begins in the font file."""
    return SFNTReader(io.BytesIO(font_bytes)).tables[tag].offset

