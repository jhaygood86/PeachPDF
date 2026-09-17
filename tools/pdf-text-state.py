#!/usr/bin/env python3
"""Report a PDF's text state from the content stream, not from a reader's summary.

Why this exists
---------------
A PDF reader hands you an *effective* font size: whatever `Tf` set, after the CTM
and the text matrix. Those are three different things, and when a document comes
out the wrong size only one of them is usually to blame:

  * a `Tf` size that is simply wrong -> a font-size resolution bug;
  * an identity `Tf` under a scaling `cm` -> a shrink-to-fit or page-fit path;
  * a form XObject with its own /Matrix -> neither, and invisible in the page stream.

`PyMuPDF`'s `span["size"]` collapses all three into one number, so a scaled page and
a mis-sized font are indistinguishable in it. Chasing one while looking at the other
is a long afternoon. This prints them apart.

Usage
-----
    python3 tools/pdf-text-state.py out.pdf [more.pdf ...]
    python3 tools/pdf-text-state.py --pages 1-3 out.pdf

Requires PyMuPDF (`python3 -m pip install pymupdf`), which CONTRIBUTING already
expects for two-renderer rasterization checks.
"""
from __future__ import annotations

import argparse
import re
import sys

try:
    import pymupdf
except ImportError:  # pragma: no cover - a missing optional dep, not a code path
    sys.exit("pdf-text-state.py needs PyMuPDF: python3 -m pip install pymupdf")

NUMBER = r"-?\d*\.?\d+"
# Operands then operator. Deliberately permissive: this reads well-formed output from
# one writer, not arbitrary PDFs, so it does not need a conforming tokenizer.
OPERATION = re.compile(
    r"((?:" + NUMBER + r"|/[^\s\[\]<>/(){}]+|\[[^\]]*\]|\([^)]*\)|<[0-9A-Fa-f\s]*>|\s)*?)\s*"
    r"([A-Za-z'\"*]+)(?=\s|$)"
)
TF = re.compile(r"/(\S+)\s+(" + NUMBER + r")\s*$")


def operations(page):
    """(operands, operator) for every operation in the page's own content streams."""
    raw = page.read_contents().decode("latin-1", "replace")
    for match in OPERATION.finditer(raw):
        yield match.group(1).strip(), match.group(2)


def numbers(operands):
    return [float(n) for n in re.findall(NUMBER, operands)]


def scan(page):
    """Non-identity `cm` scales and the Tf sizes set under them."""
    transforms, sizes, depth = [], {}, 0

    for operands, operator in operations(page):
        if operator == "q":
            depth += 1
        elif operator == "Q":
            depth = max(0, depth - 1)
        elif operator == "cm":
            matrix = numbers(operands)
            if len(matrix) == 6 and (abs(matrix[0] - 1) > 1e-9 or abs(matrix[3] - 1) > 1e-9):
                transforms.append((depth, matrix))
        elif operator == "Tf":
            font = TF.search(operands)
            if font:
                sizes.setdefault(font.group(1), set()).add(round(float(font.group(2)), 4))

    return transforms, sizes


def xobject_matrices(document, page):
    """Form XObjects the page draws, with any /Matrix of their own.

    A scale here is invisible in the page's own stream, which is the case that most
    often survives a first look.
    """
    found = []
    for name, xref in page.get_xobjects_names() if hasattr(page, "get_xobjects_names") else []:
        found.append((name, xref))
    out = []
    for item in page.get_xobjects() if hasattr(page, "get_xobjects") else []:
        xref = item[0]
        try:
            source = document.xref_object(xref, compressed=False)
        except Exception:
            continue
        matrix = re.search(r"/Matrix\s*\[([^\]]*)\]", source)
        subtype = re.search(r"/Subtype\s*/(\w+)", source)
        if matrix and subtype and subtype.group(1) == "Form":
            values = [float(v) for v in re.findall(NUMBER, matrix.group(1))]
            if len(values) == 6 and (abs(values[0] - 1) > 1e-9 or abs(values[3] - 1) > 1e-9):
                out.append((xref, values))
    return out


def report(path, pages):
    document = pymupdf.open(path)
    print(f"\n{path}")

    for number in pages:
        if number >= document.page_count:
            continue
        page = document[number]
        transforms, sizes = scan(page)
        box = tuple(round(v, 2) for v in page.rect)
        print(f"  page {number + 1}  MediaBox={box}")

        if transforms:
            print("    non-identity cm scales:")
            for depth, m in transforms[:10]:
                print(f"      depth {depth}: x={m[0]:g} y={m[3]:g}   at ({m[4]:g}, {m[5]:g})")
            if len(transforms) > 10:
                print(f"      ... and {len(transforms) - 10} more")
        else:
            print("    no non-identity cm scale")

        for form, m in xobject_matrices(document, page):
            print(f"    form XObject {form} has /Matrix scale x={m[0]:g} y={m[3]:g}")

        if sizes:
            print("    Tf sizes set in the content stream:")
            for font in sorted(sizes):
                print(f"      /{font}: {sorted(sizes[font])}")
        else:
            print("    no Tf operations")


def parse_pages(spec):
    if not spec:
        return [0]
    out = set()
    for part in spec.split(","):
        if "-" in part:
            first, last = part.split("-")
            out.update(range(int(first) - 1, int(last)))
        else:
            out.add(int(part) - 1)
    return sorted(out)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("pdf", nargs="+")
    parser.add_argument("--pages", default="1", help="1-based, e.g. 1 or 1-3 or 1,4 (default: 1)")
    args = parser.parse_args()

    pages = parse_pages(args.pages)
    for path in args.pdf:
        report(path, pages)


if __name__ == "__main__":
    main()
