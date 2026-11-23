#!/usr/bin/env python3
"""Generates src/drawing/PeachDrawing.Text/Internal/Text/CaseMappingData.g.cs from the Unicode Character Database
(Unicode 18.0.0, matching the version already checked in for Scripts.txt): UnicodeData.txt, SpecialCasing.txt, PropList.txt
and DerivedCoreProperties.txt.

Tables, all sorted so they can be binary-searched:

* SimpleUpper / SimpleLower - the one-to-one mappings of UnicodeData.txt (fields 12 and 13), as flat (code point, mapping) pairs.
* FullUpper / FullLower - the unconditional mappings of SpecialCasing.txt that map to more than one code point.
* Title - the titlecase mapping of every code point whose titlecase is not its simple uppercase: SpecialCasing.txt's full
  mappings (the German sharp s is "Ss") and UnicodeData.txt's field 14 (the digraph letters).
* CombiningClasses - the ranges of non-zero canonical combining class (UnicodeData.txt field 3), for the Lithuanian and
  Turkic context rules.
* SoftDotted - PropList.txt's Soft_Dotted.
* CasedRanges / CaseIgnorableRanges - DerivedCoreProperties.txt's Cased and Case_Ignorable, for the Final_Sigma context.

The conditional entries of SpecialCasing.txt (Final_Sigma, and the Lithuanian, Turkish and Azerbaijani rules) are context rules,
not data; they are written out in CaseMapper.cs. Plain generated C# rather than a Brotli resource: a few thousand numbers do not
justify one, and a source table works where Brotli is unavailable (WebAssembly).

Re-run whenever the .txt files are refreshed to a newer Unicode version. No third-party packages.
"""
import os
import re

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUT_PATH = os.path.normpath(os.path.join(
    SCRIPT_DIR, "..", "..", "src", "drawing", "PeachDrawing.Text", "Internal", "Text", "CaseMappingData.g.cs"))

RANGE_RE = re.compile(r'^([0-9A-Fa-f]{4,6})(?:\.\.([0-9A-Fa-f]{4,6}))?\s*;\s*(\w+)')


def version():
    with open(os.path.join(SCRIPT_DIR, "SpecialCasing.txt"), encoding="utf-8") as f:
        return re.search(r'SpecialCasing-(\d+\.\d+\.\d+)', f.readline()).group(1)


def read_unicode_data():
    """Returns (simple_upper, simple_lower, simple_title, combining) as dicts keyed by code point."""
    upper, lower, title, combining = {}, {}, {}, {}
    with open(os.path.join(SCRIPT_DIR, "UnicodeData.txt"), encoding="utf-8") as f:
        for line in f:
            fields = line.rstrip("\n").split(";")
            if len(fields) < 15:
                continue
            cp = int(fields[0], 16)
            if fields[1].endswith(", First>"):
                continue   # a range of code points with no case mappings and class 0 (CJK, Hangul, private use)
            if fields[12]:
                upper[cp] = int(fields[12], 16)
            if fields[13]:
                lower[cp] = int(fields[13], 16)
            if fields[14]:
                title[cp] = int(fields[14], 16)
            if int(fields[3]) != 0:
                combining[cp] = int(fields[3])
    return upper, lower, title, combining


def read_special_casing():
    """Returns the unconditional entries: code point -> (lower, title, upper), each a string of code points."""
    entries = {}
    with open(os.path.join(SCRIPT_DIR, "SpecialCasing.txt"), encoding="utf-8") as f:
        for line in f:
            line = line.split("#", 1)[0].strip()
            if not line:
                continue
            fields = [x.strip() for x in line.split(";")]
            if len(fields) > 4 and fields[4]:
                continue   # conditional: a context rule written out in CaseMapper.cs
            cp = int(fields[0], 16)
            as_string = lambda s: "".join(chr(int(x, 16)) for x in s.split())
            entries[cp] = (as_string(fields[1]), as_string(fields[2]), as_string(fields[3]))
    return entries


def read_ranges(file_name, prop):
    cps = []
    with open(os.path.join(SCRIPT_DIR, file_name), encoding="utf-8") as f:
        for line in f:
            if line.startswith("#"):
                continue
            m = RANGE_RE.match(line)
            if m and m.group(3) == prop:
                start = int(m.group(1), 16)
                end = int(m.group(2), 16) if m.group(2) else start
                cps.extend(range(start, end + 1))
    return coalesce(cps)


def coalesce(codepoints):
    runs = []
    for cp in sorted(set(codepoints)):
        if runs and runs[-1][1] + 1 == cp:
            runs[-1][1] = cp
        else:
            runs.append([cp, cp])
    return runs


def lit(s):
    return '"' + ''.join('\\u%04X' % ord(c) if ord(c) < 0x10000 else '\\U%08X' % ord(c) for c in s) + '"'


def main():
    upper, lower, title, combining = read_unicode_data()
    special = read_special_casing()

    full_upper = sorted((cp, v[2]) for cp, v in special.items() if len(v[2]) > 1)
    full_lower = sorted((cp, v[0]) for cp, v in special.items() if len(v[0]) > 1)

    # The titlecase of a code point: SpecialCasing's full mapping, else UnicodeData's field 14, else its simple uppercase. Only the ones
    # that are not what the full uppercase gives need a row; the rest are read from the uppercase tables.
    titles = {}
    for cp in set(upper) | set(title) | set(special):
        full_up = special[cp][2] if cp in special else chr(upper.get(cp, cp))
        full_title = special[cp][1] if cp in special else chr(title.get(cp, upper.get(cp, cp)))
        if full_title != full_up:
            titles[cp] = full_title
    title_rows = sorted(titles.items())

    combining_ranges = []
    for cp in sorted(combining):
        c = combining[cp]
        if combining_ranges and combining_ranges[-1][2] == c and combining_ranges[-1][1] == cp - 1:
            combining_ranges[-1][1] = cp
        else:
            combining_ranges.append([cp, cp, c])

    soft_dotted = [cp for a, b in read_ranges("PropList.txt", "Soft_Dotted") for cp in range(a, b + 1)]
    cased = read_ranges("DerivedCoreProperties.txt", "Cased")
    case_ignorable = read_ranges("DerivedCoreProperties.txt", "Case_Ignorable")

    out = [
        '// <auto-generated>',
        '// Generated by assets/unicode/generate_case_mapping_tables.py from the Unicode %s Character Database' % version(),
        '// (UnicodeData.txt, SpecialCasing.txt, PropList.txt, DerivedCoreProperties.txt). Do not edit.',
        '// </auto-generated>',
        'namespace PeachDrawing.Text.Internal.Text',
        '{',
        '    internal static class CaseMappingData',
        '    {',
    ]

    def pairs(name, doc, mapping):
        flat = [x for cp in sorted(mapping) for x in (cp, mapping[cp])]
        out.extend(['        /// <summary>%s</summary>' % doc, '        internal static readonly int[] %s =' % name, '        ['])
        for i in range(0, len(flat), 16):
            out.append('            ' + ', '.join('0x%X' % x for x in flat[i:i + 16]) + ',')
        out.extend(['        ];', ''])

    def strings(name, doc, rows):
        out.extend(['        /// <summary>%s</summary>' % doc, '        internal static readonly (int CodePoint, string Mapping)[] %s =' % name, '        ['])
        out.extend('            (0x%04X, %s),' % (cp, lit(s)) for cp, s in rows)
        out.extend(['        ];', ''])

    def ranges(name, doc, rows):
        out.extend(['        /// <summary>%s</summary>' % doc, '        internal static readonly (int First, int Last)[] %s =' % name, '        ['])
        out.extend('            (0x%04X, 0x%04X),' % (a, b) for a, b in rows)
        out.extend(['        ];', ''])

    pairs('SimpleUpper', 'Flat (code point, uppercase) pairs of the one-to-one mappings, sorted by code point.', upper)
    pairs('SimpleLower', 'Flat (code point, lowercase) pairs of the one-to-one mappings, sorted by code point.', lower)
    strings('FullUpper', 'The code points that uppercase to more than one code point, with the mapping.', full_upper)
    strings('FullLower', 'The code points that lowercase to more than one code point, with the mapping.', full_lower)
    strings('Title', 'The code points whose titlecase is not their simple uppercase, with the titlecase mapping.', title_rows)
    out.extend(['        /// <summary>Ranges of code points with a non-zero canonical combining class: first, last, class.</summary>',
                '        internal static readonly (int First, int Last, byte Class)[] CombiningClasses =', '        ['])
    out.extend('            (0x%04X, 0x%04X, %d),' % (a, b, c) for a, b, c in combining_ranges)
    out.extend(['        ];', ''])
    out.extend(['        /// <summary>The Soft_Dotted code points: letters whose dot a combining dot above replaces.</summary>',
                '        internal static readonly int[] SoftDotted =', '        ['])
    out.extend('            0x%04X,' % cp for cp in soft_dotted)
    out.extend(['        ];', ''])
    ranges('CasedRanges', 'The Cased property: letters that have case.', cased)
    ranges('CaseIgnorableRanges', 'The Case_Ignorable property: characters skipped when looking for the cased letters around a sigma.', case_ignorable)
    out.extend(['    }', '}', ''])

    with open(OUT_PATH, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(out))
    print("wrote %s: %d simple upper, %d simple lower, %d full upper, %d full lower, %d title, %d combining ranges, %d soft dotted, %d cased, %d case ignorable"
          % (OUT_PATH, len(upper), len(lower), len(full_upper), len(full_lower), len(title_rows), len(combining_ranges), len(soft_dotted), len(cased), len(case_ignorable)))


if __name__ == "__main__":
    main()
