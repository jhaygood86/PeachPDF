#!/usr/bin/env python3
"""Generates the compact, Brotli-compressed Indic_Syllabic_Category/Indic_Positional_Category data
resources PeachPDF embeds from the Unicode Character Database's IndicSyllabicCategory.txt/
IndicPositionalCategory.txt (Unicode 17.0.0, https://www.unicode.org/reports/tr44/), matching the
version already checked in for DerivedBidiClass.txt/VerticalOrientation.txt/Scripts.txt/
DerivedJoiningType.txt.

These two properties are HarfBuzz's own Universal Shaping Engine's raw inputs (see
gen-use-table.py in the harfbuzz repo) - PeachPDF.Text.Shaping.Use.UseCategoryClassifier derives
the final per-codepoint USE category from them (plus .NET's own built-in General_Category via
System.Globalization.CharUnicodeInfo, so no separate General_Category table needs generating here).

Output goes to src/PeachPDF/Text/Resources/Use/*.txt.br, consumed by
PeachPDF.Text.IndicSyllabicCategoryTable/IndicPositionalCategoryTable. Same run-length-encoded
(start, end, value) table shape as generate_arabic_joining_table.py/generate_script_table.py - the
one difference is each raw UCD value name (e.g. "Vowel_Dependent", "Consonant_Preceding_Repha") has
its underscores stripped before being written, so the C# reader can Enum.Parse it directly against
a PascalCase enum member name (IndicSyllabicCategory.VowelDependent, .ConsonantPrecedingRepha) with
no further translation step.

Re-run this script whenever assets/unicode/IndicSyllabicCategory.txt/IndicPositionalCategory.txt
are refreshed to a newer Unicode version.

Requires: brotli (pip install brotli)
"""
import os
import re
import brotli

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "..", "src", "PeachPDF", "Text", "Resources", "Use"))

MAX_CODEPOINT = 0x10FFFF

DATA_LINE_RE = re.compile(r'^([0-9A-Fa-f]{4,6})(?:\.\.([0-9A-Fa-f]{4,6}))?\s*;\s*(\w+)')

# Both files document every unlisted codepoint as defaulting to "Other"/"Not_Applicable"
# respectively (see each file's own header comments) - matching gen-use-table.py's own
# `defaults` tuple ('Other', 'Not_Applicable', ...).
DEFAULTS = {
    "IndicSyllabicCategory.txt": "Other",
    "IndicPositionalCategory.txt": "Not_Applicable",
}


def generate_table(filename, default_value):
    """One UCD file -> a run-length-encoded (start, end, value) table, values stripped of
    underscores so the C# side can Enum.Parse them directly."""
    path = os.path.join(SCRIPT_DIR, filename)
    values = [default_value] * (MAX_CODEPOINT + 1)

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

    lines = [f"{s:X} {e:X} {v.replace('_', '')}" for s, e, v in runs]
    return "\n".join(lines) + "\n", len(runs)


def write_compressed(name, text):
    os.makedirs(OUT_DIR, exist_ok=True)
    out_path = os.path.join(OUT_DIR, name)
    compressed = brotli.compress(text.encode("utf-8"))
    with open(out_path, "wb") as f:
        f.write(compressed)
    print(f"{name}: {len(text)} bytes raw -> {len(compressed)} bytes compressed")


def main():
    for filename, default_value in DEFAULTS.items():
        text, count = generate_table(filename, default_value)
        print(f"{filename}: {count} runs")
        write_compressed(filename.replace(".txt", "") + ".txt.br", text)


if __name__ == "__main__":
    main()
