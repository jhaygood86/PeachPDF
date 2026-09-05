#!/usr/bin/env python3
"""Generates the compact, Brotli-compressed Joining_Type data resource PeachPDF embeds from the
Unicode Character Database's extracted/DerivedJoiningType.txt (Unicode 17.0.0, UAX #44 - see
https://www.unicode.org/reports/tr44/), matching the version already checked in for
DerivedBidiClass.txt/VerticalOrientation.txt/Scripts.txt.

DerivedJoiningType.txt (not ArabicShaping.txt directly) is the source here: it already applies the
UCD's own default-value derivation - a combining mark (General_Category Mn/Me/Cf) not explicitly
listed in ArabicShaping.txt defaults to Transparent (T) rather than Non_Joining (U), which matters
for Arabic diacritics between joined letters - so this table is the ground truth Joining_Type per
codepoint without PeachPDF having to re-derive that rule from General_Category data itself.

Output goes to src/PeachPDF/Text/Resources/ArabicJoining/DerivedJoiningType.txt.br, consumed by
PeachPDF.Text.ArabicShapingTable. Same run-length-encoded (start, end, value) table shape as
generate_vertical_orientation_table.py/generate_script_table.py.

Re-run this script whenever assets/unicode/DerivedJoiningType.txt is refreshed to a newer Unicode
version.

Requires: brotli (pip install brotli)
"""
import os
import re
import brotli

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.normpath(os.path.join(
    SCRIPT_DIR, "..", "..", "src", "PeachPDF", "Text", "Resources", "ArabicJoining"))

MAX_CODEPOINT = 0x10FFFF

DATA_LINE_RE = re.compile(r'^([0-9A-Fa-f]{4,6})(?:\.\.([0-9A-Fa-f]{4,6}))?\s*;\s*(\w+)')

# The file's own header: "@missing: 0000..10FFFF; Non_Joining" - every codepoint not explicitly
# listed (including every non-Arabic-family codepoint) defaults to Non_Joining.
DEFAULT_VALUE = "U"

# DerivedJoiningType.txt spells each value out as a single letter already (U/R/D/C/L/T) - unlike
# Scripts.txt's full names, so no translation table is needed here.


def generate_joining_type_table():
    """DerivedJoiningType.txt -> a run-length-encoded (start, end, value) table."""
    path = os.path.join(SCRIPT_DIR, "DerivedJoiningType.txt")
    values = [DEFAULT_VALUE] * (MAX_CODEPOINT + 1)

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
    text, count = generate_joining_type_table()
    print(f"DerivedJoiningType: {count} runs")
    write_compressed("DerivedJoiningType.txt.br", text)


if __name__ == "__main__":
    main()
