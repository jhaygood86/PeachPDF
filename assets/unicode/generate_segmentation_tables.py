#!/usr/bin/env python3
"""Generates src/PeachDrawing.Text/Internal/Text/Segmentation/SegmentationData.g.cs from the Unicode Character Database
(Unicode 18.0.0): the property values that UAX #14 (line breaking) and UAX #29 (grapheme cluster, word and sentence
boundaries) read.

Sources, all in this directory:

* LineBreak.txt - the Line_Break class, plus the General_Category of each listed code point from the comment after each
  entry (Pi and Pf and Mn/Mc and Cn are what the line breaking rules ask about)
* EastAsianWidth.txt - the wide, fullwidth and halfwidth code points (LB19a and LB30 exclude them)
* emoji-data.txt - Extended_Pictographic (LB30b, GB11 and WB3c)
* GraphemeBreakProperty.txt, DerivedCoreProperties.txt (Indic_Conjunct_Break, for GB9c)
* WordBreakProperty.txt, SentenceBreakProperty.txt

Each property is one complete table over U+0000..U+10FFFF: a sorted array of range starts and a parallel array of values
(a range runs to the next start). Plain generated C#, not a Brotli resource, for the reason generate_emoji_table.py gives:
a source table works where Brotli is unavailable (WebAssembly). Re-run whenever the .txt files are refreshed to a newer
Unicode version. No third-party packages.
"""
import os
import re

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUT_PATH = os.path.normpath(os.path.join(
    SCRIPT_DIR, "..", "..", "src", "PeachDrawing.Text", "Internal", "Text", "Segmentation", "SegmentationData.g.cs"))

MAX = 0x110000
LINE_RE = re.compile(r'^([0-9A-Fa-f]{4,6})(?:\.\.([0-9A-Fa-f]{4,6}))?\s*;\s*([^#;]+?)\s*(?:;\s*([^#]+?)\s*)?(?:#\s*(\S+).*)?$')

LB_CLASSES = ["XX", "AI", "AK", "AL", "AP", "AS", "B2", "BA", "BB", "BK", "CB", "CJ", "CL", "CM", "CP", "CR", "EB", "EM", "EX",
              "GL", "H2", "H3", "HH", "HL", "HY", "ID", "IN", "IS", "JL", "JT", "JV", "LF", "NL", "NS", "NU", "OP", "PO", "PR",
              "QU", "RI", "SA", "SG", "SP", "SY", "VF", "VI", "WJ", "ZW", "ZWJ"]
GCB_CLASSES = ["Other", "CR", "LF", "Control", "Extend", "ZWJ", "Regional_Indicator", "Prepend", "SpacingMark", "L", "V", "T", "LV", "LVT"]
WB_CLASSES = ["Other", "CR", "LF", "Newline", "Extend", "ZWJ", "Regional_Indicator", "Format", "Katakana", "Hebrew_Letter", "ALetter",
              "Single_Quote", "Double_Quote", "MidNumLet", "MidLetter", "MidNum", "Numeric", "ExtendNumLet", "WSegSpace"]
SB_CLASSES = ["Other", "CR", "LF", "Extend", "Sep", "Format", "Sp", "Lower", "Upper", "OLetter", "Numeric", "ATerm", "SContinue", "STerm", "Close"]
INCB = {"None": 0, "Linker": 1, "Consonant": 2, "Extend": 3}

# flag bits of the line break table, above the six bits of the class
LB_PI, LB_PF, LB_MARK, LB_CN, LB_WIDE, LB_EXTPICT = 1 << 6, 1 << 7, 1 << 8, 1 << 9, 1 << 10, 1 << 11
GCB_EXTPICT = 1 << 5
WB_EXTPICT = 1 << 5


def read_ranges(name):
    """Yields (start, end, field1, field2, comment) for each data line of a UCD file."""
    with open(os.path.join(SCRIPT_DIR, name), encoding="utf-8") as f:
        for line in f:
            if line.startswith("#") or not line.strip():
                continue
            m = LINE_RE.match(line.rstrip("\n"))
            if not m:
                raise SystemExit("%s: cannot read the line %r" % (name, line))
            start = int(m.group(1), 16)
            end = int(m.group(2), 16) if m.group(2) else start
            yield start, end, m.group(3), m.group(4), m.group(5)


def index_of(classes, name, file):
    try:
        return classes.index(name)
    except ValueError:
        raise SystemExit("%s: unknown value %r (regenerate the class list)" % (file, name))


def fill(values, start, end, value, mode="set"):
    for cp in range(start, end + 1):
        values[cp] = value if mode == "set" else values[cp] | value


def ext_pict():
    result = set()
    for start, end, prop, _, _ in read_ranges("emoji-data.txt"):
        if prop == "Extended_Pictographic":
            result.update(range(start, end + 1))
    return result


def build_line_break(pict):
    values = [0] * MAX
    for start, end, cls, _, comment in read_ranges("LineBreak.txt"):
        v = index_of(LB_CLASSES, cls, "LineBreak.txt")
        gc = comment or ""
        if gc == "Pi":
            v |= LB_PI
        if gc == "Pf":
            v |= LB_PF
        if gc in ("Mn", "Mc"):
            v |= LB_MARK
        if gc == "Cn":
            v |= LB_CN
        fill(values, start, end, v)
    # a code point the file does not list is unassigned as far as the rules are concerned
    listed = [False] * MAX
    for start, end, _, _, _ in read_ranges("LineBreak.txt"):
        for cp in range(start, end + 1):
            listed[cp] = True
    for cp in range(MAX):
        if not listed[cp]:
            values[cp] |= LB_CN
    for start, end, ea, _, _ in read_ranges("EastAsianWidth.txt"):
        if ea in ("F", "W", "H"):
            fill(values, start, end, LB_WIDE, "or")
    for cp in pict:
        values[cp] |= LB_EXTPICT
    return values


def build_simple(file, classes, pict_bit=0, pict=None):
    values = [0] * MAX
    for start, end, cls, _, _ in read_ranges(file):
        fill(values, start, end, index_of(classes, cls, file))
    if pict_bit:
        for cp in pict:
            values[cp] |= pict_bit
    return values


def build_grapheme(pict):
    values = build_simple("GraphemeBreakProperty.txt", GCB_CLASSES, GCB_EXTPICT, pict)
    for start, end, prop, value, _ in read_ranges("DerivedCoreProperties.txt"):
        if prop == "InCB" and value in INCB:
            fill(values, start, end, INCB[value] << 6, "or")
    return values


def coalesce(values):
    starts, vals = [], []
    for cp, v in enumerate(values):
        if not vals or vals[-1] != v:
            starts.append(cp)
            vals.append(v)
    return starts, vals


def emit_array(kind, name, items, per_row, fmt):
    rows = []
    for i in range(0, len(items), per_row):
        rows.append("            " + ", ".join(fmt % x for x in items[i:i + per_row]) + ",")
    return "        private static ReadOnlySpan<%s> %s =>\n        [\n%s\n        ];\n" % (kind, name, "\n".join(rows))


def emit_table(title, values):
    starts, vals = coalesce(values)
    return (emit_array("int", title + "Starts", starts, 8, "0x%X") + "\n" +
            emit_array("ushort", title + "Values", vals, 12, "0x%X"))


def emit_enum(name, classes):
    lines = "".join("            %s = %d,\n" % (c.replace("_", ""), i) for i, c in enumerate(classes))
    return "    internal enum %s : byte\n    {\n%s    }\n" % (name, lines)


def check(values):
    """Fails loudly when a source file changed shape enough that a flag would silently disappear."""
    assert len(LB_CLASSES) < 64 and len(GCB_CLASSES) < 32 and len(WB_CLASSES) < 32 and len(SB_CLASSES) < 32
    assert values["line_break"][0xAB] & LB_PI and values["line_break"][0xBB] & LB_PF, "Pi/Pf lost: LineBreak.txt comments changed?"
    assert values["line_break"][0x0E31] & LB_MARK, "Mn/Mc lost: LineBreak.txt comments changed?"
    assert values["line_break"][0x0378] & LB_CN, "Cn lost: LineBreak.txt comments changed?"
    assert values["line_break"][0x1F600] & LB_EXTPICT and values["grapheme"][0x1F600] & GCB_EXTPICT
    assert (values["grapheme"][0x094D] >> 6) & 3 == INCB["Linker"], "InCB lost: DerivedCoreProperties.txt changed?"


def main():
    pict = ext_pict()
    line_break = build_line_break(pict)
    grapheme = build_grapheme(pict)
    word = build_simple("WordBreakProperty.txt", WB_CLASSES, WB_EXTPICT, pict)
    sentence = build_simple("SentenceBreakProperty.txt", SB_CLASSES)
    check({"line_break": line_break, "grapheme": grapheme})

    out = (
        "// <auto-generated>\n"
        "// Generated by assets/unicode/generate_segmentation_tables.py from Unicode 18.0.0's LineBreak.txt, EastAsianWidth.txt,\n"
        "// emoji-data.txt, GraphemeBreakProperty.txt, DerivedCoreProperties.txt, WordBreakProperty.txt and\n"
        "// SentenceBreakProperty.txt. Do not edit by hand; re-run the script.\n"
        "// </auto-generated>\n"
        "\n"
        "using System;\n"
        "\n"
        "namespace PeachDrawing.Text.Internal.Text.Segmentation\n"
        "{\n"
        + emit_enum("LineBreakClass", LB_CLASSES) + "\n"
        + emit_enum("GraphemeBreakClass", GCB_CLASSES) + "\n"
        + emit_enum("WordBreakClass", WB_CLASSES) + "\n"
        + emit_enum("SentenceBreakClass", SB_CLASSES) + "\n"
        "    internal static partial class SegmentationData\n"
        "    {\n"
        "        // Line break table value: the class in bits 0-5, then these flags.\n"
        "        internal const int LineBreakPi = 1 << 6;\n"
        "        internal const int LineBreakPf = 1 << 7;\n"
        "        internal const int LineBreakMark = 1 << 8;\n"
        "        internal const int LineBreakUnassigned = 1 << 9;\n"
        "        internal const int LineBreakEastAsian = 1 << 10;\n"
        "        internal const int LineBreakPictographic = 1 << 11;\n"
        "        // Grapheme table value: the class in bits 0-4, Extended_Pictographic in bit 5, Indic_Conjunct_Break in bits 6-7.\n"
        "        internal const int GraphemePictographic = 1 << 5;\n"
        "        // Word table value: the class in bits 0-4, Extended_Pictographic in bit 5.\n"
        "        internal const int WordPictographic = 1 << 5;\n"
        "\n"
        + emit_table("LineBreak", line_break) + "\n"
        + emit_table("Grapheme", grapheme) + "\n"
        + emit_table("Word", word) + "\n"
        + emit_table("Sentence", sentence) +
        "    }\n"
        "}\n")
    with open(OUT_PATH, "w", encoding="utf-8", newline="\n") as f:
        f.write(out)
    for name, values in (("LineBreak", line_break), ("Grapheme", grapheme), ("Word", word), ("Sentence", sentence)):
        print("%s: %d ranges" % (name, len(coalesce(values)[0])))


if __name__ == "__main__":
    main()
