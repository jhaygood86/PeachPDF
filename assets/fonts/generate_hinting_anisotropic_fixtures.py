#!/usr/bin/env python3
"""Generates HintingAnisotropic.golden.json.gz: what FreeType itself makes of a handful of bundled TrueType and CFF fonts at
non-square pixel sizes (x_ppem != y_ppem), for the non-square-pixel paths of the ports (Current_Ratio, the stretched cvt/ppem
routines, the non-square branches of MD/MDRP/IP for TrueType; the independent x_scale/y_scale of Adobe's CFF engine) to be
compared with, exactly, in 26.6 units. Also records a square-pixel run of the same fonts and sizes (the larger of the pair,
used for both axes), so the same file proves the equal-axis path is unaffected by the two-axis code (a regression guard).

FreeType computes x_ppem and y_ppem independently from FT_Set_Char_Size's four arguments (char_width, char_height,
horz_resolution, vert_resolution): most of the sizes below vary char_width against char_height at a common 72 dpi (which
gives an exact, easily-read ppem in 26.6), and one representative size instead keeps char_width equal to char_height and
varies horz_resolution against vert_resolution, to exercise that specific call shape too - both reach the same
FT_Request_Metrics computation, so nothing about the interpreter's own code path differs between them.

Usage (see generate_hinting_golden.py for --freetype):

  python generate_hinting_anisotropic_fixtures.py --freetype path/to/freetype.dll

Requires freetype-py and fontTools (for reading the bundled WOFF files as plain sfnt).
"""
import argparse
import ctypes
import gzip
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "HintingAnisotropic.golden.json.gz")

LATIN = "".join(chr(c) for c in range(0x21, 0x7F))

# (kind, file, glyph selection or limit): a real hinted TrueType font, the synthetic opcode-exercising one, a real CFF font
# and the synthetic hint-exercising CFF one - the same fixtures the square-pixel goldens already use.
TT_FONTS = [
    ("LiberationSans-Regular.woff", LATIN),
    ("HintingOpcodes.ttf", None),
]
TT_GLYPH_LIMIT = 90

CFF_FONTS = [
    ("SourceCodePro-Regular.otf", 60),
    ("HintingCff.otf", 60),
]

# (char_width, char_height, horz_resolution, vert_resolution), all as their real meaning to FT_Set_Char_Size; the ppems in
# 26.6 that result are what the C# side asks TtSize/CffSize for. The pair (72, 72) resolution with unequal sizes is the
# common shape; the last entry instead keeps the size equal and stretches through the resolution arguments.
SIZE_CASES = [
    (12 * 64, 16 * 64, 72, 72),
    (16 * 64, 12 * 64, 72, 72),
    (9 * 64, 9 * 64 + 32, 72, 72),          # less than a pixel apart in 26.6, still different once rounded
    (24 * 64, 18 * 64, 72, 72),
    (14 * 64 + 32, 10 * 64 + 16, 72, 72),   # fractional both ways
    (20 * 64, 20 * 64, 72, 72),             # square, recorded through the same anisotropic call shape (the guard)
    (16 * 64, 16 * 64, 72, 144),            # square char size, stretched through the resolution arguments instead
]

FT_LOAD_NO_BITMAP = 0x8
FT_LOAD_NO_AUTOHINT = 0x8000


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


def ppem_of(size, resolution):
    """What FT_Request_Metrics makes of one axis: FT_MulDiv(size, resolution, 72) in 26.6, then rounded to a whole ppem the
    way the port's own Reset() rounds it - recorded so the C# test asks TtSize/CffSize for exactly the ppem FreeType used,
    not a value that could round differently because of a different intermediate step."""
    return (size * resolution + 36) // 72


def record_truetype(freetype, raw, data, glyphs, cases):
    modes = {"standard": 40, "monochrome": 35}
    result = {}
    for mode_name, interpreter in modes.items():
        runs = []
        for (w, h, hres, vres) in cases:
            lib = freetype.FT_Library()
            assert raw.FT_Init_FreeType(ctypes.byref(lib)) == 0
            version = ctypes.c_uint(interpreter)
            assert raw._lib.FT_Property_Set(lib, b"truetype", b"interpreter-version", ctypes.byref(version)) == 0
            buf = ctypes.create_string_buffer(data, len(data))
            face = freetype.FT_Face()
            assert raw.FT_New_Memory_Face(lib, buf, len(data), 0, ctypes.byref(face)) == 0
            flags = FT_LOAD_NO_BITMAP | FT_LOAD_NO_AUTOHINT | (2 << 16 if mode_name == "monochrome" else 0)

            per_glyph = {}
            for gid in glyphs:
                assert raw.FT_Set_Char_Size(face, w, h, hres, vres) == 0
                error = raw.FT_Load_Glyph(face, gid, flags)
                if error:
                    per_glyph[str(gid)] = {"err": error}
                    continue
                slot = face.contents.glyph.contents
                outline = slot.outline
                n = outline.n_points & 0xFFFF
                per_glyph[str(gid)] = {
                    "a": slot.advance.x,
                    "e": [outline.contours[i] & 0xFFFF for i in range(outline.n_contours & 0xFFFF)],
                    "x": [outline.points[i].x for i in range(n)],
                    "y": [outline.points[i].y for i in range(n)],
                    "t": [outline.tags[i] & 1 for i in range(n)],
                }

            raw.FT_Done_Face(face)
            raw.FT_Done_FreeType(lib)
            runs.append({"sizeX": ppem_of(w, hres), "sizeY": ppem_of(h, vres), "glyphs": per_glyph})
        result[mode_name] = runs
    return result


def record_cff(freetype, raw, data, glyphs, cases):
    """What FreeType makes of each glyph, for every size: a fresh library and face for each glyph (as
    generate_hinting_cff_fixtures.py does), because the `random` Type 2 operator draws from a generator whose state a
    glyph leaves for the next one, and the port starts every glyph from the font's own initialRandomSeed instead."""
    runs = []
    for (w, h, hres, vres) in cases:
        per_glyph = {}
        for gid in glyphs:
            lib = freetype.FT_Library()
            assert raw.FT_Init_FreeType(ctypes.byref(lib)) == 0
            seed = ctypes.c_int(0)
            assert raw._lib.FT_Property_Set(lib, b"cff", b"random-seed", ctypes.byref(seed)) == 0
            engine = ctypes.c_uint(1)  # FT_HINTING_ADOBE
            assert raw._lib.FT_Property_Set(lib, b"cff", b"hinting-engine", ctypes.byref(engine)) == 0
            buf = ctypes.create_string_buffer(data, len(data))
            face = freetype.FT_Face()
            assert raw.FT_New_Memory_Face(lib, buf, len(data), 0, ctypes.byref(face)) == 0
            assert raw.FT_Set_Char_Size(face, w, h, hres, vres) == 0
            error = raw.FT_Load_Glyph(face, gid, FT_LOAD_NO_BITMAP | FT_LOAD_NO_AUTOHINT)
            if error:
                per_glyph[str(gid)] = {"err": error}
            else:
                slot = face.contents.glyph.contents
                outline = slot.outline
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
        runs.append({"sizeX": ppem_of(w, hres), "sizeY": ppem_of(h, vres), "glyphs": per_glyph})
    return {"standard": runs}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--freetype", help="path to a FreeType shared library built from the tag the ports derive from")
    parser.add_argument("--allow-other-version", action="store_true")
    parser.add_argument("--out", default=OUT)
    args = parser.parse_args()

    freetype, raw = load_freetype(args.freetype)
    version = freetype.version()
    if version != (2, 14, 3) and not args.allow_other_version:
        sys.exit("FreeType %d.%d.%d loaded; the ports derive from 2.14.3 (see --freetype)" % version)

    result = {
        "freetype": {"version": "%d.%d.%d" % version, "tag": "VER-2-14-3"},
        "format": "per glyph: a=advance (26.6), e=contour end points, x/y=coordinates (26.6), t=1 (TrueType) or 1/2 (CFF: on-curve/cubic-control) for the point tag, err=FreeType's error",
        "fonts": [],
    }

    for file_name, chars in TT_FONTS:
        data = sfnt_bytes(os.path.join(HERE, file_name))
        buf = ctypes.create_string_buffer(data, len(data))
        lib = freetype.FT_Library()
        assert raw.FT_Init_FreeType(ctypes.byref(lib)) == 0
        face = freetype.FT_Face()
        assert raw.FT_New_Memory_Face(lib, buf, len(data), 0, ctypes.byref(face)) == 0
        if chars is None:
            glyphs = list(range(min(face.contents.num_glyphs, TT_GLYPH_LIMIT)))
        else:
            glyphs = []
            for ch in chars:
                gid = raw.FT_Get_Char_Index(face, ord(ch))
                if gid and gid not in glyphs:
                    glyphs.append(gid)
        raw.FT_Done_Face(face)
        raw.FT_Done_FreeType(lib)

        modes = record_truetype(freetype, raw, data, glyphs, SIZE_CASES)
        result["fonts"].append({"kind": "tt", "file": file_name, "glyphs": glyphs, "modes": modes})
        print(file_name, "(tt) glyphs:", len(glyphs))

    for file_name, limit in CFF_FONTS:
        data = sfnt_bytes(os.path.join(HERE, file_name))
        glyphs = list(range(limit))
        modes = record_cff(freetype, raw, data, glyphs, SIZE_CASES)
        result["fonts"].append({"kind": "cff", "file": file_name, "glyphs": glyphs, "modes": modes})
        print(file_name, "(cff) glyphs:", len(glyphs))

    payload = json.dumps(result, separators=(",", ":")).encode("utf-8")
    with gzip.GzipFile(args.out, "wb", mtime=0) as f:
        f.write(payload)
    print("wrote", args.out, os.path.getsize(args.out), "bytes")


if __name__ == "__main__":
    main()
