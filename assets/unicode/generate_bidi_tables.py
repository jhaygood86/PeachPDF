#!/usr/bin/env python3
"""Generates the compact, Brotli-compressed bidi data resources PeachPDF embeds from the
Unicode Character Database source files in this directory (DerivedBidiClass.txt,
BidiBrackets.txt, BidiMirroring.txt - Unicode 17.0.0, see PR #542).

Output goes to src/PeachPDF/Text/Resources/Bidi/*.txt.br, consumed by
PeachPDF.Text.Bidi.BidiClassTable/BidiBrackets/BidiMirroring. Each output file is a plain-text
table (one record per line, fields space-separated, hex codepoints) - the same
"plain text, Brotli-compressed" shape as the existing hyphenation pattern resources
(Text/Resources/Patterns/*.txt.br) - Brotli-compressed with the .NET-compatible raw Brotli
stream format (System.IO.Compression.BrotliStream).

Re-run this script whenever assets/unicode/*.txt is refreshed to a newer Unicode version.

Requires: brotli (pip install brotli)
"""
import os
import re
import brotli

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "..", "src", "PeachPDF", "Text", "Resources", "Bidi"))

# The four @missing default-value names DerivedBidiClass.txt's own header documents (see the
# comments above each "# @missing: RANGE; Name" line) - the only spelled-out class names that
# appear in this file's @missing lines, as opposed to the two/three-letter abbreviations used by
# every actual data line.
MISSING_NAME_TO_ABBR = {
    "Left_To_Right": "L",
    "Right_To_Left": "R",
    "Arabic_Letter": "AL",
    "European_Terminator": "ET",
}

MAX_CODEPOINT = 0x10FFFF

DATA_LINE_RE = re.compile(r'^([0-9A-Fa-f]{4,6})(?:\.\.([0-9A-Fa-f]{4,6}))?\s*;\s*([A-Za-z]+)')
MISSING_LINE_RE = re.compile(r'^#\s*@missing:\s*([0-9A-Fa-f]{4,6})\.\.([0-9A-Fa-f]{4,6})\s*;\s*([A-Za-z_]+)')


def generate_bidi_class_table():
    """DerivedBidiClass.txt -> a run-length-encoded (start, end, class) table.

    Per the file's own header: every codepoint not covered by an explicit data line defaults to
    L, except for a handful of @missing-documented ranges (Hebrew/Arabic-ish blocks default to
    R/AL, the Currency Symbols block defaults to ET) which this parses from the file's own
    "# @missing: RANGE; Name" comment lines rather than hand-transcribing them. One further
    default this does NOT reproduce: unassigned codepoints inside Default_Ignorable_Code_Point or
    Noncharacter_Code_Point ranges are documented as defaulting to BN rather than L - those
    codepoints never appear in real rendered text, so approximating them as the file's own
    top-level default (L) is an acceptable, documented simplification rather than a real Bidi_Class
    lookup requirement.
    """
    path = os.path.join(SCRIPT_DIR, "DerivedBidiClass.txt")
    classes = ["L"] * (MAX_CODEPOINT + 1)

    missing_ranges = []
    data_ranges = []

    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            m = MISSING_LINE_RE.match(line)
            if m:
                start, end, name = m.group(1), m.group(2), m.group(3)
                abbr = MISSING_NAME_TO_ABBR.get(name)
                if abbr:
                    missing_ranges.append((int(start, 16), int(end, 16), abbr))
                continue

            if line.startswith("#") or not line.strip():
                continue

            m = DATA_LINE_RE.match(line)
            if m:
                start = int(m.group(1), 16)
                end = int(m.group(2), 16) if m.group(2) else start
                data_ranges.append((start, end, m.group(3)))

    # Apply broader @missing defaults first, then explicit per-codepoint data (which always wins).
    for start, end, cls in missing_ranges:
        for cp in range(start, end + 1):
            classes[cp] = cls
    for start, end, cls in data_ranges:
        for cp in range(start, end + 1):
            classes[cp] = cls

    # Coalesce consecutive same-class codepoints into runs.
    runs = []
    run_start = 0
    run_class = classes[0]
    for cp in range(1, MAX_CODEPOINT + 1):
        if classes[cp] != run_class:
            runs.append((run_start, cp - 1, run_class))
            run_start = cp
            run_class = classes[cp]
    runs.append((run_start, MAX_CODEPOINT, run_class))

    lines = [f"{s:X} {e:X} {c}" for s, e, c in runs]
    return "\n".join(lines) + "\n", len(runs)


def generate_bidi_brackets():
    """BidiBrackets.txt -> "codepoint paired-codepoint type" lines (type: o/c)."""
    path = os.path.join(SCRIPT_DIR, "BidiBrackets.txt")
    out = []
    line_re = re.compile(r'^([0-9A-Fa-f]{4,6})\s*;\s*([0-9A-Fa-f]{4,6})\s*;\s*([oc])\b')
    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            m = line_re.match(line)
            if m:
                out.append(f"{m.group(1).upper()} {m.group(2).upper()} {m.group(3)}")
    return "\n".join(out) + "\n", len(out)


def generate_bidi_mirroring():
    """BidiMirroring.txt -> "codepoint mirror-codepoint" lines."""
    path = os.path.join(SCRIPT_DIR, "BidiMirroring.txt")
    out = []
    line_re = re.compile(r'^([0-9A-Fa-f]{4,6})\s*;\s*([0-9A-Fa-f]{4,6})')
    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            m = line_re.match(line)
            if m:
                out.append(f"{m.group(1).upper()} {m.group(2).upper()}")
    return "\n".join(out) + "\n", len(out)


def write_compressed(name, text):
    os.makedirs(OUT_DIR, exist_ok=True)
    out_path = os.path.join(OUT_DIR, name)
    compressed = brotli.compress(text.encode("utf-8"))
    with open(out_path, "wb") as f:
        f.write(compressed)
    print(f"{name}: {len(text)} bytes raw -> {len(compressed)} bytes compressed")


def main():
    text, count = generate_bidi_class_table()
    print(f"DerivedBidiClass: {count} runs")
    write_compressed("DerivedBidiClass.txt.br", text)

    text, count = generate_bidi_brackets()
    print(f"BidiBrackets: {count} pairs")
    write_compressed("BidiBrackets.txt.br", text)

    text, count = generate_bidi_mirroring()
    print(f"BidiMirroring: {count} pairs")
    write_compressed("BidiMirroring.txt.br", text)


if __name__ == "__main__":
    main()
