#!/usr/bin/env python3
"""Generates HintingGolden.json.gz: what FreeType itself makes of the bundled TrueType fonts at a grid of sizes, the
reference the port of its bytecode interpreter (PeachDrawing.Text/Internal/Hinting/FreeType) is compared with, exactly, in
26.6 pixel units. Any difference is a bug in the port, never a tolerance question.

For every (font, mode, size, glyph) it records what FT_Load_Glyph leaves in the glyph slot: the outline points (x and y in 26.6),
whether each is an on-curve point, the contour ends and the advance (the hinted advance, rounded to a pixel, as FreeType reports it).

Modes (the two the public API offers, and the two other combinations the port supports):

  standard    interpreter version 40 ("minimal subpixel hinting", FreeType's default), FT_LOAD_TARGET_NORMAL
  monochrome  interpreter version 35, FT_LOAD_TARGET_MONO
  v40mono     interpreter version 40 with FT_LOAD_TARGET_MONO (backward compatibility off)
  v35normal   interpreter version 35 with FT_LOAD_TARGET_NORMAL (grayscale flag set)

Every glyph is loaded from a size that has just been requested again, so that FreeType runs the CVT program afresh for it: a
FreeType size keeps the twilight zone, and a WCVTF, that a glyph program leaves behind for the next glyph, and the port (by design)
does not. Requesting the size each time gives FreeType the same independence.

The FreeType this needs is the one the port was made from. freetype-py bundles an older FreeType on some platforms; point
--freetype at a shared library built from the VER-2-14-3 tag (the script refuses to run against any other version unless
--allow-other-version is given, and records the version it used in the file):

  python generate_hinting_golden.py --freetype path/to/freetype.dll

Requires freetype-py and fontTools (for reading the bundled WOFF files as plain sfnt). Run from anywhere.
"""
import argparse
import ctypes
import gzip
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "HintingGolden.json.gz")

# (file, glyph selection) - the selection is a string of characters, resolved through the font's own cmap.
LATIN = "".join(chr(c) for c in range(0x21, 0x7F)) + "Ã Ã¡Ã¢Ã£Ã¤Ã¥Ã§Ã¨Ã©ÃªÃ«Ã¬Ã­Ã®Ã¯Ã±Ã²Ã³Ã´ÃµÃ¶Ã¹ÃºÃ»Ã¼Ã€ÃÃ‰Ã‘Ã–ÃœÃŸÃ¦Å“Â©Â®"
FONTS = [
    ("LiberationSans-Regular.woff", LATIN),
    ("LiberationSerif-Regular.woff", LATIN),
    ("LiberationMono-Bold.woff", LATIN[:60]),
    ("LiberationSans-BoldItalic.woff", LATIN[:60]),
    ("SourceSans3-Regular.ttf", LATIN[:60]),
    ("NotoSansHebrewSubset.ttf", None),      # every glyph of the subset
    ("RecursiveSubset.ttf", None),
]

# the most glyphs taken from a font that is recorded whole
GLYPH_LIMIT = 90

# sizes in 26.6 pixels per em (font size in points at 72 dpi): whole ppems, and fractional sizes that exercise the scale
# rounding of fonts that ask for integer ppems (head flags bit 3) and of those that do not.
SIZES = [9 * 64, 11 * 64, 12 * 64, 14 * 64 + 32, 16 * 64, 22 * 64, 30 * 64, 48 * 64, 64 * 64 + 16]

MODES = {
    # name: (interpreter version, target mono?, sizes to record)
    "standard": (40, False, SIZES),
    "monochrome": (35, True, SIZES),
    "v40mono": (40, True, [12 * 64, 16 * 64]),
    "v35normal": (35, False, [12 * 64, 16 * 64]),
}

FT_LOAD_NO_BITMAP = 0x8
FT_LOAD_NO_AUTOHINT = 0x8000
FT_LOAD_TARGET_MONO = 2 << 16


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
    """The bytes of a font file as plain sfnt (the bundled Liberation fonts are WOFF)."""
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


class Library:
    def __init__(self, raw, freetype, interpreter):
        self.raw = raw
        self.handle = freetype.FT_Library()
        assert raw.FT_Init_FreeType(ctypes.byref(self.handle)) == 0
        version = ctypes.c_uint(interpreter)
        error = raw._lib.FT_Property_Set(self.handle, b"truetype", b"interpreter-version", ctypes.byref(version))
        assert error == 0, "FT_Property_Set failed: %d" % error

    def close(self):
        self.raw.FT_Done_FreeType(self.handle)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--freetype", help="path to a FreeType shared library built from the tag the port derives from")
    parser.add_argument("--allow-other-version", action="store_true")
    parser.add_argument("--out", default=OUT)
    args = parser.parse_args()

    freetype, raw = load_freetype(args.freetype)
    version = freetype.version()
    if version != (2, 14, 3) and not args.allow_other_version:
        sys.exit("FreeType %d.%d.%d loaded; the port derives from 2.14.3 (see --freetype)" % version)

    result = {
        "freetype": {"version": "%d.%d.%d" % version, "tag": "VER-2-14-3"},
        "format": "per glyph: a=advance (26.6), e=contour end points, x/y=coordinates (26.6), t=1 for an on-curve point",
        "fonts": [],
    }

    for file_name, chars in FONTS:
        data = sfnt_bytes(os.path.join(HERE, file_name))
        buf = ctypes.create_string_buffer(data, len(data))
        font_entry = {"file": file_name, "modes": {}}

        for mode_name, (interpreter, mono, sizes) in MODES.items():
            lib = Library(raw, freetype, interpreter)
            face = freetype.FT_Face()
            assert raw.FT_New_Memory_Face(lib.handle, buf, len(data), 0, ctypes.byref(face)) == 0
            flags = FT_LOAD_NO_BITMAP | FT_LOAD_NO_AUTOHINT | (FT_LOAD_TARGET_MONO if mono else 0)

            if chars is None:
                glyphs = list(range(min(face.contents.num_glyphs, GLYPH_LIMIT)))
            else:
                glyphs = []
                for ch in chars:
                    gid = raw.FT_Get_Char_Index(face, ord(ch))
                    if gid and gid not in glyphs:
                        glyphs.append(gid)

            if "glyphs" not in font_entry:
                font_entry["glyphs"] = glyphs

            runs = []
            for size in sizes:
                per_glyph = {}
                for gid in glyphs:
                    # a fresh size for each glyph, so FreeType runs the CVT program again (see the module comment)
                    assert raw.FT_Set_Char_Size(face, size, size, 72, 72) == 0
                    error = raw.FT_Load_Glyph(face, gid, flags)
                    if error:
                        per_glyph[str(gid)] = {"err": error}
                        continue
                    slot = face.contents.glyph.contents
                    outline = slot.outline
                    n = outline.n_points
                    pts = [(outline.points[i].x, outline.points[i].y) for i in range(n)]
                    tags = [outline.tags[i] & 1 for i in range(n)]
                    ends = [outline.contours[i] for i in range(outline.n_contours)]
                    per_glyph[str(gid)] = {
                        "a": slot.advance.x,
                        "e": ends,
                        "x": [p[0] for p in pts],
                        "y": [p[1] for p in pts],
                        "t": tags,
                    }
                runs.append({"size": size, "glyphs": per_glyph})
            font_entry["modes"][mode_name] = runs

            raw.FT_Done_Face(face)
            lib.close()

        result["fonts"].append(font_entry)
        print(file_name, "glyphs:", len(font_entry["glyphs"]))

    payload = json.dumps(result, separators=(",", ":")).encode("utf-8")
    with gzip.GzipFile(args.out, "wb", mtime=0) as f:
        f.write(payload)
    print("wrote", args.out, os.path.getsize(args.out), "bytes")


if __name__ == "__main__":
    main()
