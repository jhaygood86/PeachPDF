#!/usr/bin/env python3
"""Generates the compact, Brotli-compressed Vertical_Orientation data resource PeachPDF embeds
from the Unicode Character Database's VerticalOrientation.txt (Unicode 17.0.0, UAX #50 - see
https://www.unicode.org/reports/tr50/), matching the version already checked in for
DerivedBidiClass.txt/BidiBrackets.txt/BidiMirroring.txt (see generate_bidi_tables.py).

Output goes to src/PeachPDF/Text/Resources/VerticalOrientation/VerticalOrientation.txt.br,
consumed by PeachPDF.Text.VerticalOrientationTable. Same "plain text, Brotli-compressed,
run-length-encoded (start, end, value) table" shape as generate_bidi_tables.py's own
DerivedBidiClass.txt output - same generator idiom, sibling script rather than folding into that
one, since this is a different UAX/property with its own single-range @missing default (R) rather
than DerivedBidiClass.txt's several block-scoped ones.

Re-run this script whenever assets/unicode/VerticalOrientation.txt is refreshed to a newer
Unicode version.

Requires: brotli (pip install brotli)
"""
import os
import re
import brotli

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.normpath(os.path.join(
    SCRIPT_DIR, "..", "..", "src", "PeachPDF", "Text", "Resources", "VerticalOrientation"))

MAX_CODEPOINT = 0x10FFFF

DATA_LINE_RE = re.compile(r'^([0-9A-Fa-f]{4,6})(?:\.\.([0-9A-Fa-f]{4,6}))?\s*;\s*([A-Za-z]+)')
MISSING_LINE_RE = re.compile(r'^#\s*@missing:\s*([0-9A-Fa-f]{4,6})\.\.([0-9A-Fa-f]{4,6})\s*;\s*([A-Za-z]+)')


def generate_vertical_orientation_table():
    """VerticalOrientation.txt -> a run-length-encoded (start, end, value) table.

    The file's own single "# @missing: 0000..10FFFF; R" line is the default for every codepoint
    not covered by an explicit data line; explicit data lines (U/R/Tu/Tr) then override it.
    """
    path = os.path.join(SCRIPT_DIR, "VerticalOrientation.txt")
    values = ["R"] * (MAX_CODEPOINT + 1)

    data_ranges = []

    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            if line.startswith("#") or not line.strip():
                continue

            m = DATA_LINE_RE.match(line)
            if m:
                start = int(m.group(1), 16)
                end = int(m.group(2), 16) if m.group(2) else start
                data_ranges.append((start, end, m.group(3)))

    for start, end, value in data_ranges:
        for cp in range(start, end + 1):
            values[cp] = value

    # Coalesce consecutive same-value codepoints into runs.
    runs = []
    run_start = 0
    run_value = values[0]
    for cp in range(1, MAX_CODEPOINT + 1):
        if values[cp] != run_value:
            runs.append((run_start, cp - 1, run_value))
            run_start = cp
            run_value = values[cp]
    runs.append((run_start, MAX_CODEPOINT, run_value))

    lines = [f"{s:X} {e:X} {v}" for s, e, v in runs]
    return "\n".join(lines) + "\n", len(runs)


def write_compressed(name, text):
    os.makedirs(OUT_DIR, exist_ok=True)
    out_path = os.path.join(OUT_DIR, name)
    compressed = brotli.compress(text.encode("utf-8"))
    with open(out_path, "wb") as f:
        f.write(compressed)
    print(f"{name}: {len(text)} bytes raw -> {len(compressed)} bytes compressed")


def main():
    text, count = generate_vertical_orientation_table()
    print(f"VerticalOrientation: {count} runs")
    write_compressed("VerticalOrientation.txt.br", text)


if __name__ == "__main__":
    main()
