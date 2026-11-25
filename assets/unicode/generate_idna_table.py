#!/usr/bin/env python3
"""Generates the compact, Brotli-compressed UTS #46 mapping table PeachBrackets.Scripting embeds (its URL parser converts host names with it), from
the Unicode data checked in beside this script (Unicode 18.0.0, matching Scripts.txt and the others):

* IdnaMappingTable.txt  - UTS #46's mapping table (https://www.unicode.org/reports/tr46/): per code point range a status
  (valid, mapped, deviation, ignored, disallowed) and, for mapped, the replacement code points.
* UnicodeData.txt       - the canonical combining class (field 3) of each code point; class 9 is the virama set RFC 5892's ContextJ rules need.

Output goes to src/engine/PeachBrackets.Scripting/Internal/Idna/idna-uts46.bin, read by PeachBrackets.Scripting.Internal.Idna.Uts46. Re-run this
script whenever IdnaMappingTable.txt or UnicodeData.txt is refreshed to a newer Unicode version. The two files are fetched from
https://www.unicode.org/Public/<version>/idna/IdnaMappingTable.txt and https://www.unicode.org/Public/<version>/ucd/UnicodeData.txt.

File format (all numbers unsigned LEB128 varints; the whole file is Brotli-compressed):
  version string length, version string (ASCII)
  range count, then per range: start code point (delta from the previous start), status byte ('V' valid, 'M' mapped, 'D' deviation,
      'I' ignored, 'X' disallowed), and for 'M' the replacement length and its code points. A range runs to the next range's start.
  virama count, then each virama code point (delta from the previous)

Requires: brotli (pip install brotli)
"""
import os
import re
import brotli

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUT_FILE = os.path.normpath(os.path.join(
    SCRIPT_DIR, "..", "..", "src", "engine", "PeachBrackets.Scripting", "Internal", "Idna", "idna-uts46.bin"))

MAX_CODEPOINT = 0x10FFFF
STATUS_LETTERS = {"valid": "V", "mapped": "M", "deviation": "D", "ignored": "I", "disallowed": "X"}
LINE_RE = re.compile(r"^([0-9A-Fa-f]{4,6})(?:\.\.([0-9A-Fa-f]{4,6}))?\s*;\s*(\w+)\s*(?:;\s*([0-9A-Fa-f ]*?))?\s*(?:;[^#]*)?(?:#.*)?$")
VERSION_RE = re.compile(r"^#\s*Version:\s*(\S+)")


def varint(n, out):
    while True:
        b = n & 0x7F
        n >>= 7
        if n:
            out.append(b | 0x80)
        else:
            out.append(b)
            return


def read_mapping():
    version = None
    ranges = []  # (start, end, letter, replacement or None)
    with open(os.path.join(SCRIPT_DIR, "IdnaMappingTable.txt"), encoding="utf-8") as f:
        for line in f:
            if version is None and (m := VERSION_RE.match(line)):
                version = m.group(1)
            if not line.strip() or line.startswith("#"):
                continue
            m = LINE_RE.match(line.rstrip("\n"))
            if not m:
                raise SystemExit(f"unrecognized line: {line!r}")
            start = int(m.group(1), 16)
            end = int(m.group(2), 16) if m.group(2) else start
            status = m.group(3)
            if status.startswith("disallowed_STD3"):
                raise SystemExit("this generator expects the table without STD3 statuses (Unicode 15.1 and later)")
            letter = STATUS_LETTERS[status]
            replacement = [int(x, 16) for x in m.group(4).split()] if letter == "M" else None
            ranges.append((start, end, letter, replacement))
    if version is None:
        raise SystemExit("IdnaMappingTable.txt names no version")
    ranges.sort()

    # The table covers every code point; check, so a malformed or partial file does not give a silently wrong table.
    expected = 0
    for start, end, _, _ in ranges:
        if start != expected:
            raise SystemExit(f"gap or overlap at {expected:04X} (next range starts at {start:04X})")
        expected = end + 1
    if expected != MAX_CODEPOINT + 1:
        raise SystemExit(f"the table ends at {expected - 1:04X}, not {MAX_CODEPOINT:04X}")
    return version, ranges


def read_viramas():
    viramas = []
    first = None
    with open(os.path.join(SCRIPT_DIR, "UnicodeData.txt"), encoding="utf-8") as f:
        for line in f:
            fields = line.rstrip("\n").split(";")
            cp = int(fields[0], 16)
            if fields[1].endswith(", First>"):
                first = cp
                continue
            low = first if fields[1].endswith(", Last>") and first is not None else cp
            first = None
            if fields[3] == "9":
                viramas.extend(range(low, cp + 1))
    return viramas


def main():
    version, ranges = read_mapping()
    viramas = read_viramas()

    out = bytearray()
    varint(len(version), out)
    out += version.encode("ascii")

    # Neighbouring ranges of the same status with no replacement run together; a mapped range keeps its own replacement.
    merged = []
    for start, end, letter, replacement in ranges:
        if replacement is None and merged and merged[-1][1] == letter and merged[-1][2] is None:
            continue
        # A mapped range of several code points maps each to its own replacement only when the file lists them separately, so a range stays one entry.
        merged.append((start, letter, replacement))

    varint(len(merged), out)
    previous = 0
    for start, letter, replacement in merged:
        varint(start - previous, out)
        previous = start
        out.append(ord(letter))
        if letter == "M":
            varint(len(replacement), out)
            for cp in replacement:
                varint(cp, out)

    varint(len(viramas), out)
    previous = 0
    for cp in viramas:
        varint(cp - previous, out)
        previous = cp

    data = brotli.compress(bytes(out), quality=11)
    os.makedirs(os.path.dirname(OUT_FILE), exist_ok=True)
    with open(OUT_FILE, "wb") as f:
        f.write(data)
    print(f"Unicode {version}: {len(merged)} ranges, {len(viramas)} viramas, {len(out)} -> {len(data)} bytes ({OUT_FILE})")


if __name__ == "__main__":
    main()
