#!/usr/bin/env python3
"""Generates the word lists PeachDrawing.Text breaks Thai, Lao, Khmer and Burmese lines with (UAX #14 class SA,
Complex_Context: the scripts write no spaces between words, so a line breaking algorithm needs a dictionary).

Source: the break-iterator dictionaries of ICU, icu4c/source/data/brkitr/dictionaries/{thaidict,laodict,khmerdict,burmesedict}.txt
in https://github.com/unicode-org/icu, fetched from ONE pinned release tag (ICU_TAG below) and checked against a SHA-256 each, so
regenerating gives the same word lists (the DEFLATE bytes themselves can differ with the zlib build Python uses; the payload inside them cannot). They are the dictionaries browsers and Android segment with. cjdict.txt is not used:
Chinese and Japanese break by UAX #14's own rules.

Licences (read at that tag, in its LICENSE file): the Thai and Khmer lists are ICU data under the Unicode License v3; laodict.txt
and burmesedict.txt carry BSD-style licences of their own (Brian Eugene Wilson and Robert Martin Campbell, 2013; LeRoy Benjamin
Sharon, 2013). All four notices are reproduced in src/PeachDrawing.Text/THIRD-PARTY-LICENSES.md.

Output, one file per script in src/PeachDrawing.Text/Internal/Text/Resources/Dictionaries/ (<script>.dict), embedded in the
assembly and read by PeachDrawing.Text.Internal.Text.Segmentation.WordDictionary:

    DEFLATE (raw, RFC 1951: not Brotli, which WebAssembly has no decoder for) of
        "PDW1"                  4 bytes, format tag
        base                    uint16 little endian: the code point that byte 0 stands for
        count                   uint32 little endian: the number of words
        shared[count]           bytes: how many characters the word has in common with the one before it
        suffixes                for each word, the characters after the shared start, then a 0 byte
    A character is a byte: its code point minus `base`, plus one (so that 0 ends a word). Words are NFC, without the zero width joiner and non-joiner some Khmer and
    Burmese entries spell (the reader ignores them in the text, too, so a word matches with or without one), deduplicated, and sorted
    by code point, which is what lets a reader find every word that is a prefix of some text by narrowing a range of the list one
    character at a time.

Usage: python generate_dictionary_breaking.py [--source DIR]      (DIR holds the four .txt files; default: download from the tag)
"""
import argparse
import hashlib
import os
import struct
import sys
import unicodedata
import urllib.request
import zlib

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.normpath(os.path.join(
    SCRIPT_DIR, "..", "..", "src", "PeachDrawing.Text", "Internal", "Text", "Resources", "Dictionaries"))

ICU_TAG = "release-78.3"   # commit 21d1eb0f306e1141c10931e914dfc038c06121da
ICU_URL = "https://raw.githubusercontent.com/unicode-org/icu/{tag}/icu4c/source/data/brkitr/dictionaries/{name}.txt"

# script -> (ICU file, SHA-256 of that file at the tag, code point of byte 0, first and last code point a word may use)
SCRIPTS = {
    "thai": ("thaidict", "3166abde40c0f44ab91c28f5ce96d7d1472cb7882e1c0bda0a72f8f69dba4274", 0x0E00, 0x0E01, 0x0E5B),
    "lao": ("laodict", "3c876934a3fa81031d2333525eafaca6a7c9f842e3b98f18c38880420afb5d36", 0x0E80, 0x0E81, 0x0EDF),
    "khmer": ("khmerdict", "87bee2d17cd5148aa36957eb05409eefc124de8ad519b81b789298ef3e60b5d9", 0x1780, 0x1780, 0x17FF),
    "burmese": ("burmesedict", "61d8abc3d9102b2f9bf0c9f44db0d7ab89b18172d8cd26832e4c83174bd8673b", 0x1000, 0x1000, 0x109F),
}


def fetch(name, source):
    if source:
        with open(os.path.join(source, name + ".txt"), "rb") as f:
            return f.read()
    with urllib.request.urlopen(ICU_URL.format(tag=ICU_TAG, name=name)) as response:
        return response.read()


def read_words(data, first, last):
    text = data.decode("utf-8-sig")
    words = set()
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        word = unicodedata.normalize("NFC", line.split("\t")[0].strip()).replace("‌", "").replace("‍", "")
        for ch in word:
            cp = ord(ch)
            if not first <= cp <= last:
                raise SystemExit("U+%04X in %r is outside the script" % (cp, word))
        if word:
            words.add(word)
    return sorted(words, key=lambda w: [ord(c) for c in w])


def encode(words, base):
    def byte_of(ch):
        value = ord(ch) - base + 1
        assert 1 <= value < 0x100
        return value

    shared, suffixes = bytearray(), bytearray()
    previous = ""
    for word in words:
        common = 0
        while common < len(previous) and common < len(word) and previous[common] == word[common]:
            common += 1
        assert len(word) < 256
        shared.append(common)
        suffixes.extend(byte_of(ch) for ch in word[common:])
        suffixes.append(0)
        previous = word

    payload = b"PDW1" + struct.pack("<HI", base, len(words)) + bytes(shared) + bytes(suffixes)
    compressor = zlib.compressobj(9, zlib.DEFLATED, -15)
    return compressor.compress(payload) + compressor.flush()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", help="a directory holding thaidict.txt, laodict.txt, khmerdict.txt and burmesedict.txt")
    args = parser.parse_args()

    os.makedirs(OUT_DIR, exist_ok=True)
    total = 0
    for script, (name, digest, base, first, last) in SCRIPTS.items():
        data = fetch(name, args.source)
        if hashlib.sha256(data).hexdigest() != digest:
            raise SystemExit("%s.txt does not match the file at %s" % (name, ICU_TAG))
        words = read_words(data, first, last)
        blob = encode(words, base)
        with open(os.path.join(OUT_DIR, script + ".dict"), "wb") as f:
            f.write(blob)
        total += len(blob)
        print("%-8s %6d words  %7d bytes  longest %d" % (script, len(words), len(blob), max(len(w) for w in words)))
    print("total %d bytes" % total)


if __name__ == "__main__":
    sys.exit(main())
