#!/usr/bin/env python3
"""Generates HintingGasp.ttf and HintingGasp.golden.json.gz: a small synthetic TrueType font with a `gasp` table (which sizes want
grid-fitting), and what FreeType 2.14.3 says about it, for the reading of `gasp` in PeachDrawing.Text
(Internal/Hinting/FreeType/TtGasp.cs) and the decision it drives to be compared with FreeType exactly.

FreeType does not itself decide anything from `gasp`: it loads the table and answers FT_Get_Gasp(face, ppem) with the flags of the first
range whose maxPPEM is at least ppem (with only the two low bits of a version 0 table kept, and -1, FT_GASP_NO_TABLE, when there is no
table, it is of an unsupported version, or no range reaches the size). What to do with the flags is the caller's; PeachDrawing.Text grid-fits
a size only when FT_GASP_DO_GRIDFIT (1) is set for it, or when there is nothing to say (see the doc comment of FT_GASP_DO_GRIDFIT in ftgasp.h).
So the reference here is (1) FT_Get_Gasp for a set of table variants over every ppem from 0 to 300 and a few larger ones, and (2) what
FT_Load_Glyph makes of the glyphs of the font at a set of sizes (FreeType hints at every size, whatever `gasp` says), so that a test can tell
"the outline the API gives at a size the table allows" (it must equal FreeType's) from "at one it does not" (it must be the scaled design).

The variants are the same font with another `gasp` table, written byte by byte: a normal version 1 table; version 0 with flag bits that
version 0 does not have (FreeType masks them); a version 2 table (FreeType ignores it); a table with no ranges; a table whose last range ends
below the largest sizes; ranges that are not in ascending order; a single range covering everything with no flags; and no table at all.

The font is CC0 (it contains no third-party data; see HintingGasp.LICENSE.txt). It is deterministic.

Usage (see generate_hinting_golden.py for --freetype):

  python generate_hinting_gasp_fixtures.py --freetype path/to/freetype.dll

Requires freetype-py and fontTools.
"""
import argparse
import base64
import ctypes
import gzip
import io
import json
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import generate_hinting_golden as golden  # noqa: E402  (the FreeType loader and the library wrapper are shared)

OUT_FONT = os.path.join(HERE, "HintingGasp.ttf")
OUT_GOLDEN = os.path.join(HERE, "HintingGasp.golden.json.gz")

FT_LOAD_NO_BITMAP = 0x8
FT_LOAD_NO_AUTOHINT = 0x8000

# the ranges of the font's own table (version 1): (maxPPEM, flags)
#   1 - 8     0x0002  grayscale only              -> no grid-fitting
#   9 - 14    0x0001  grid-fitting                -> fitted
#  15 - 20    0x0003  grid-fitting and grayscale  -> fitted
#  21 - 40    0x000C  ClearType flags only        -> no grid-fitting for standard rasterization
#  41 - 65535 0x000F  everything                  -> fitted
MAIN_RANGES = [(8, 0x0002), (14, 0x0001), (20, 0x0003), (40, 0x000C), (0xFFFF, 0x000F)]

# the sizes (whole pixels per em) at which the outlines of the glyphs are recorded: in every range of the table
SIZES = [6, 8, 9, 12, 14, 15, 18, 20, 21, 30, 40, 41, 60]

# the ppem values FT_Get_Gasp is asked for
PPEMS = list(range(0, 301)) + [500, 1000, 65535]

# glyph programs: SVTCA[y], then MDAP[rnd] of the four points, then IUP[y]: a point that is not on a whole pixel is moved onto one
PROGRAM = bytes([0x00, 0xB0, 0x00, 0x2F, 0xB0, 0x01, 0x2F, 0xB0, 0x02, 0x2F, 0xB0, 0x03, 0x2F, 0x30])


def gasp_bytes(version, ranges, count=None):
    return struct.pack(">HH", version, len(ranges) if count is None else count) + b"".join(struct.pack(">HH", m, f) for m, f in ranges)


def build_font(gasp):
    """The font, with `gasp` as the bytes of its gasp table (None: no such table)."""
    from fontTools.fontBuilder import FontBuilder
    from fontTools.pens.ttGlyphPen import TTGlyphPen
    from fontTools.ttLib import TTFont
    from fontTools.ttLib.tables import ttProgram
    from fontTools.ttLib.sfnt import SFNTReader, SFNTWriter

    def rectangle(height, hinted):
        pen = TTGlyphPen(None)
        pen.moveTo((0, 0))
        pen.lineTo((0, height))
        pen.lineTo((500, height))
        pen.lineTo((500, 0))
        pen.closePath()
        glyph = pen.glyph()
        if hinted:
            glyph.program = ttProgram.Program()
            glyph.program.fromBytecode(PROGRAM)
        return glyph

    names = [".notdef", "A", "B"]
    fb = FontBuilder(1000, isTTF=True)
    fb.font.recalcTimestamp = False
    fb.font.recalcBBoxes = False  # the maximum profile and the bounding boxes are set below
    fb.setupGlyphOrder(names)
    fb.setupCharacterMap({0x41: "A", 0x42: "B"})
    fb.setupGlyf({".notdef": rectangle(600, False), "A": rectangle(697, True), "B": rectangle(733, True)})
    fb.setupHorizontalMetrics({n: (600, 0) for n in names})
    fb.setupHorizontalHeader(ascent=800, descent=-200)
    fb.setupNameTable({"familyName": "HintingGasp", "styleName": "Regular"})
    fb.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
    fb.setupPost()
    head = fb.font["head"]
    head.created = head.modified = 3406620153  # 2011-12-13 11:22:33, as FontBuilder's own default
    head.flags |= 8  # integer ppem, as almost every hinted font has
    head.xMin, head.yMin, head.xMax, head.yMax = 0, 0, 500, 733
    maxp = fb.font["maxp"]
    maxp.maxSizeOfInstructions = len(PROGRAM)  # a font that has glyph programs says how long the longest is: that is how the port tells a hinted font
    maxp.maxStackElements = 2
    maxp.maxPoints, maxp.maxContours = 4, 1

    buf = io.BytesIO()
    fb.font.save(buf)
    buf.seek(0)
    reader = SFNTReader(buf)
    tables = {tag: reader[tag] for tag in reader.tables}
    if gasp is not None:
        tables["gasp"] = gasp
    out = io.BytesIO()
    writer = SFNTWriter(out, len(tables), reader.sfntVersion)
    for tag in sorted(tables):
        writer[tag] = tables[tag]
    writer.close()
    return out.getvalue()


def variants():
    """(name, gasp table bytes or None)."""
    return [
        ("version-1", gasp_bytes(1, MAIN_RANGES)),
        ("version-0-with-higher-bits", gasp_bytes(0, [(10, 0xFFFF), (30, 0x00FD), (0xFFFF, 0x0006)])),
        ("version-0", gasp_bytes(0, [(9, 0x0000), (0xFFFF, 0x0003)])),
        ("version-2", gasp_bytes(2, MAIN_RANGES)),
        ("no-ranges", gasp_bytes(1, [])),
        ("last-range-ends-at-100", gasp_bytes(1, [(12, 0x0001), (100, 0x0002)])),
        ("ranges-out-of-order", gasp_bytes(1, [(50, 0x0001), (20, 0x0002), (0xFFFF, 0x0001)])),
        ("one-range-without-flags", gasp_bytes(1, [(0xFFFF, 0x0000)])),
        ("one-range-of-zero-size", gasp_bytes(1, [(0, 0x0001), (0xFFFF, 0x0000)])),
        ("no-table", None),
    ]


def gasp_of(raw, face, ppem):
    function = raw._lib.FT_Get_Gasp
    function.restype = ctypes.c_int
    function.argtypes = [ctypes.c_void_p, ctypes.c_uint]
    return function(ctypes.cast(face, ctypes.c_void_p), ppem)


def open_face(freetype, raw, data, interpreter=40):
    lib = golden.Library(raw, freetype, interpreter)
    buf = ctypes.create_string_buffer(data, len(data))
    face = freetype.FT_Face()
    assert raw.FT_New_Memory_Face(lib.handle, buf, len(data), 0, ctypes.byref(face)) == 0
    return lib, face, buf


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--freetype", help="path to a FreeType shared library built from the tag the port derives from")
    parser.add_argument("--allow-other-version", action="store_true")
    parser.add_argument("--out", default=OUT_GOLDEN)
    args = parser.parse_args()

    freetype, raw = golden.load_freetype(args.freetype)
    version = freetype.version()
    if version != (2, 14, 3) and not args.allow_other_version:
        sys.exit("FreeType %d.%d.%d loaded; the port derives from 2.14.3 (see --freetype)" % version)

    main_font = build_font(gasp_bytes(1, MAIN_RANGES))
    with open(OUT_FONT, "wb") as f:
        f.write(main_font)
    print("wrote", OUT_FONT, len(main_font), "bytes")

    result = {
        "freetype": {"version": "%d.%d.%d" % version, "tag": "VER-2-14-3"},
        "format": "ppems: the sizes FT_Get_Gasp is asked for; variants[].flags: its answers (-1 is FT_GASP_NO_TABLE); glyphs: per glyph and size (whole "
                  "pixels per em), a=advance (26.6), e=contour end points, x/y=coordinates (26.6), t=1 for an on-curve point, err=FreeType's error",
        "font": "HintingGasp.ttf",
        "ppems": PPEMS,
        "sizes": SIZES,
        "variants": [],
        "glyphs": {},
    }

    for name, table in variants():
        data = build_font(table)
        lib, face, _buf = open_face(freetype, raw, data)
        flags = [gasp_of(raw, face, ppem) for ppem in PPEMS]
        raw.FT_Done_Face(face)
        lib.close()
        result["variants"].append({"name": name, "font": base64.b64encode(data).decode("ascii"), "flags": flags})
        print(name, "ranges:", sorted(set(flags)))

    lib, face, _buf = open_face(freetype, raw, main_font)
    for gid in (1, 2):
        per_size = {}
        for size in SIZES:
            # a fresh size for each glyph, so that FreeType runs the CVT program again (see generate_hinting_golden.py)
            assert raw.FT_Set_Char_Size(face, size * 64, size * 64, 72, 72) == 0
            error = raw.FT_Load_Glyph(face, gid, FT_LOAD_NO_BITMAP | FT_LOAD_NO_AUTOHINT)
            if error:
                per_size[str(size)] = {"err": error}
                continue
            slot = face.contents.glyph.contents
            outline = slot.outline
            n = outline.n_points
            per_size[str(size)] = {
                "a": slot.advance.x,
                "e": [outline.contours[i] for i in range(outline.n_contours)],
                "x": [outline.points[i].x for i in range(n)],
                "y": [outline.points[i].y for i in range(n)],
                "t": [outline.tags[i] & 1 for i in range(n)],
            }
        result["glyphs"][str(gid)] = per_size
    raw.FT_Done_Face(face)
    lib.close()

    with gzip.GzipFile(args.out, "wb", mtime=0) as f:
        f.write(json.dumps(result, separators=(",", ":")).encode("utf-8"))
    print("wrote", args.out, os.path.getsize(args.out), "bytes")


if __name__ == "__main__":
    main()
