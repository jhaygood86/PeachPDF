#!/usr/bin/env python3
"""Render one HTML file with Chrome and with PeachPDF, and diff the word geometry.

Why this exists
---------------
"What does a browser do with this?" is the question every layout question ends at,
and answering it by eye on two PDFs is both slow and bad at the differences that
matter most: a word that moved two points, or a line that wrapped where the other
engine kept it whole. Those are invisible to a text comparison — the characters are
identical either way — and easy to miss in a side-by-side render.

This lines the two up word by word and reports the drift, so a wrap shows up as a
large y jump and a sizing difference shows up as drift that accumulates.

Usage
-----
    python3 tools/compare-with-chrome.py page.html
    python3 tools/compare-with-chrome.py page.html --peachpdf 'dotnet run --project src/PeachPDF.Cli -c Release --'
    python3 tools/compare-with-chrome.py page.html --keep /tmp/out   # keep both PDFs

Requires PyMuPDF and a Chrome/Chromium on PATH (or --chrome).
"""
from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

try:
    import pymupdf
except ImportError:  # pragma: no cover
    sys.exit("compare-with-chrome.py needs PyMuPDF: python3 -m pip install pymupdf")

CHROME_NAMES = ["google-chrome", "google-chrome-stable", "chromium", "chromium-browser", "chrome"]


def find_chrome(explicit):
    if explicit:
        return explicit
    for name in CHROME_NAMES:
        found = shutil.which(name)
        if found:
            return found
    sys.exit("no Chrome/Chromium on PATH; pass --chrome")


def render_chrome(binary, html: Path, out: Path):
    subprocess.run(
        [binary, "--headless", "--disable-gpu", "--no-sandbox", "--no-pdf-header-footer",
         "--run-all-compositor-stages-before-draw", f"--print-to-pdf={out}", html.resolve().as_uri()],
        capture_output=True,
    )
    return out.exists()


def render_peachpdf(command: str, html: Path, out: Path):
    line = f"{command} {html} -o {out}"
    result = subprocess.run(line, shell=True, capture_output=True, text=True)
    if not out.exists():
        print(result.stdout[-600:], result.stderr[-600:], file=sys.stderr)
    return out.exists()


def words(pdf: Path):
    """(page, x0, y0, text) for every word, in reading order."""
    document = pymupdf.open(pdf)
    out = []
    for number, page in enumerate(document):
        for x0, y0, _x1, _y1, text, *_ in page.get_text("words"):
            out.append((number, x0, y0, text))
    return out


def align(left, right):
    """Pair words by text, in order of appearance, so an extra word does not desync."""
    seen: dict[str, int] = {}
    index: dict[tuple[str, int], tuple] = {}
    for entry in left:
        key = entry[3]
        index[(key, seen.get(key, 0))] = entry
        seen[key] = seen.get(key, 0) + 1

    seen.clear()
    pairs = []
    for entry in right:
        key = entry[3]
        occurrence = seen.get(key, 0)
        seen[key] = occurrence + 1
        counterpart = index.get((key, occurrence))
        if counterpart:
            pairs.append((counterpart, entry))
    return pairs


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("html")
    parser.add_argument("--chrome", help="Chrome/Chromium binary (default: first found on PATH)")
    parser.add_argument("--peachpdf", default="peachpdf",
                        help="how to invoke the CLI (default: peachpdf); the input path "
                             "and -o OUTPUT are appended, matching docs/cli.md's grammar")
    parser.add_argument("--threshold", type=float, default=1.0,
                        help="report a word once it differs by more than this many points (default: 1)")
    parser.add_argument("--keep", help="directory to keep both PDFs in")
    args = parser.parse_args()

    html = Path(args.html)
    workdir = Path(args.keep) if args.keep else Path(tempfile.mkdtemp())
    workdir.mkdir(parents=True, exist_ok=True)
    chrome_pdf, peach_pdf = workdir / "chrome.pdf", workdir / "peachpdf.pdf"

    if not render_chrome(find_chrome(args.chrome), html, chrome_pdf):
        sys.exit("Chrome produced no PDF")
    if not render_peachpdf(args.peachpdf, html, peach_pdf):
        sys.exit("PeachPDF produced no PDF")

    chrome_words, peach_words = words(chrome_pdf), words(peach_pdf)
    chrome_pages = pymupdf.open(chrome_pdf).page_count
    peach_pages = pymupdf.open(peach_pdf).page_count

    print(f"  chrome:   {chrome_pages} page(s), {len(chrome_words)} words")
    print(f"  peachpdf: {peach_pages} page(s), {len(peach_words)} words")
    if chrome_pages != peach_pages:
        print(f"  PAGE COUNT DIFFERS ({chrome_pages} vs {peach_pages})")

    pairs = align(chrome_words, peach_words)
    if not pairs:
        sys.exit("  no words could be paired; the two renders share no text")

    moved = [(c, p) for c, p in pairs
             if abs(p[1] - c[1]) > args.threshold or abs(p[2] - c[2]) > args.threshold
             or p[0] != c[0]]

    print(f"  paired {len(pairs)} words; {len(moved)} moved more than {args.threshold}pt")
    if moved:
        print(f"  {'word':22s} {'chrome (pg,x,y)':>26s} {'peachpdf (pg,x,y)':>26s} {'dx':>7s} {'dy':>7s}")
        for chrome_word, peach_word in moved[:25]:
            print(f"  {chrome_word[3][:22]:22s} "
                  f"{f'({chrome_word[0]+1}, {chrome_word[1]:.1f}, {chrome_word[2]:.1f})':>26s} "
                  f"{f'({peach_word[0]+1}, {peach_word[1]:.1f}, {peach_word[2]:.1f})':>26s} "
                  f"{peach_word[1]-chrome_word[1]:+7.1f} {peach_word[2]-chrome_word[2]:+7.1f}")
        if len(moved) > 25:
            print(f"  ... and {len(moved) - 25} more")

    if args.keep:
        print(f"  PDFs kept in {workdir}")


if __name__ == "__main__":
    main()
