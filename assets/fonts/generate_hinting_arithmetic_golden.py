#!/usr/bin/env python3
"""Generates HintingArithmetic.golden.json.gz: what FreeType's own fixed-point functions FT_MulFix, FT_DivFix, FT_MulDiv and
FT_Vector_Length return for a set of inputs (the corner cases of the 32-bit `FT_Long`, and random values in the ranges the interpreter
uses), the reference the port of FreeType's arithmetic (PeachDrawing.Text/Internal/Hinting/FreeType/FtCalc.cs) is compared with.

The port is bit-exact with FreeType built for a 32-bit `long` and 64-bit intermediates, which is what the FreeType shared library used to
make this data is (a Windows build); see generate_hinting_golden.py for how to point the script at it.

  python generate_hinting_arithmetic_golden.py --freetype path/to/freetype.dll
"""
import argparse
import ctypes
import gzip
import json
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "HintingArithmetic.golden.json.gz")

INT_MIN = -2 ** 31
INT_MAX = 2 ** 31 - 1

EDGES = [0, 1, -1, 2, -2, 63, 64, 65, -64, 0x7FFF, 0x8000, -0x8000, 0xFFFF, 0x10000, -0x10000, 0x10001, 0x4000, -0x4000,
         0x2000, 0x3FFF, 0x5A82, 0x7FFFFFFF, -0x7FFFFFFF, INT_MIN, INT_MAX, 0x12345678, -0x12345678, 1000000, -1000000, 26 * 64, 2048 * 64]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--freetype")
    parser.add_argument("--allow-other-version", action="store_true")
    args = parser.parse_args()

    if args.freetype:
        real = ctypes.CDLL

        def patched(name, *a, **k):
            if isinstance(name, str) and os.path.basename(name).lower().startswith("libfreetype"):
                name = os.path.abspath(args.freetype)
            return real(name, *a, **k)

        ctypes.CDLL = patched

    import freetype
    from freetype import raw
    version = freetype.version()
    if version != (2, 14, 3) and not args.allow_other_version:
        sys.exit("FreeType %d.%d.%d loaded; the port derives from 2.14.3 (see --freetype)" % version)

    lib = raw._lib
    long_ = ctypes.c_long
    assert ctypes.sizeof(long_) == 4, "this data is for a 32-bit FT_Long"
    for name, argc in (("FT_MulFix", 2), ("FT_DivFix", 2), ("FT_MulDiv", 3)):
        fn = getattr(lib, name)
        fn.restype = long_
        fn.argtypes = [long_] * argc

    class Vector(ctypes.Structure):
        _fields_ = [("x", long_), ("y", long_)]

    lib.FT_Vector_Length.restype = long_
    lib.FT_Vector_Length.argtypes = [ctypes.POINTER(Vector)]

    rng = random.Random(0xA817)

    def value():
        r = rng.random()
        if r < 0.3:
            return rng.choice(EDGES)
        if r < 0.6:
            return rng.randrange(-4096 * 64, 4096 * 64)      # pixel-ish 26.6 values
        if r < 0.8:
            return rng.randrange(-0x20000, 0x20000)         # 16.16 scales
        return rng.randrange(INT_MIN, INT_MAX)

    mulfix = [(a, b, lib.FT_MulFix(a, b)) for a, b in [(value(), value()) for _ in range(2500)] + [(a, b) for a in EDGES for b in EDGES]]
    divfix = [(a, b, lib.FT_DivFix(a, b)) for a, b in [(value(), value()) for _ in range(2500)] + [(a, b) for a in EDGES for b in EDGES]]
    muldiv = [(a, b, c, lib.FT_MulDiv(a, b, c)) for a, b, c in [(value(), value(), value()) for _ in range(3000)]
              + [(a, b, c) for a in EDGES[:16] for b in EDGES[:16] for c in EDGES[:12]]]

    length = []
    vectors = [(value(), value()) for _ in range(2500)] + [(a, b) for a in EDGES for b in EDGES] + \
              [(rng.randrange(-0x4000, 0x4000), rng.randrange(-0x4000, 0x4000)) for _ in range(1500)]
    for x, y in vectors:
        v = Vector(x, y)
        length.append((x, y, lib.FT_Vector_Length(ctypes.byref(v))))

    result = {
        "freetype": {"version": "%d.%d.%d" % version, "tag": "VER-2-14-3"},
        "mulfix": mulfix, "divfix": divfix, "muldiv": muldiv, "length": length,
    }
    with gzip.GzipFile(OUT, "wb", mtime=0) as f:
        f.write(json.dumps(result, separators=(",", ":")).encode("utf-8"))
    print("wrote", OUT, os.path.getsize(OUT), "bytes;", len(mulfix), len(divfix), len(muldiv), len(length), "cases")


if __name__ == "__main__":
    main()
