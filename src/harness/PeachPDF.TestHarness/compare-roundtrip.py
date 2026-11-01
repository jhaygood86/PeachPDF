#!/usr/bin/env python3
"""Compares showcase PDFs with their PDF-importer round trips.

Usage:
    dotnet run --project PeachPDF.TestHarness -c Release -- <showcaseDir>                      # writes the showcase PDFs
    dotnet run --project PeachPDF.TestHarness -c Release -- --import-roundtrip <showcaseDir> <roundtripDir>
    python3 compare-roundtrip.py <showcaseDir> <roundtripDir> [--dpi 60] [--threshold 0.02] [--diffs <dir>]

Every PDF is rasterized with both MuPDF and PDFium, original and round trip, page by page. A page is reported when the share of
pixels that differ noticeably (any channel by more than 24/255) between the original and the round trip exceeds --threshold in
either renderer. MuPDF and PDFium also differ from each other on the originals; that baseline is printed so a failure can be
judged against it. Requires: pip install pymupdf pypdfium2 pillow numpy

Exit status is 1 when any page is over the threshold, so it can gate a run.
"""
import argparse
import os
import sys

import fitz  # PyMuPDF
import numpy as np
import pypdfium2 as pdfium
from PIL import Image


def raster_mupdf(path, dpi):
    doc = fitz.open(path)
    pages = []
    for page in doc:
        pix = page.get_pixmap(dpi=dpi, alpha=False)
        pages.append(np.frombuffer(pix.samples, dtype=np.uint8).reshape(pix.height, pix.width, pix.n)[:, :, :3])
    return pages


def raster_pdfium(path, dpi):
    pdf = pdfium.PdfDocument(path)
    pages = []
    for page in pdf:
        img = page.render(scale=dpi / 72.0).to_pil().convert("RGB")
        pages.append(np.asarray(img))
    return pages


def differing_share(a, b):
    if a.shape != b.shape:
        h, w = min(a.shape[0], b.shape[0]), min(a.shape[1], b.shape[1])
        a, b = a[:h, :w], b[:h, :w]
        size_mismatch = True
    else:
        size_mismatch = False
    delta = np.abs(a.astype(np.int16) - b.astype(np.int16)).max(axis=2)
    return float((delta > 24).mean()), size_mismatch, delta


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("original_dir")
    parser.add_argument("roundtrip_dir")
    parser.add_argument("--dpi", type=int, default=60)
    parser.add_argument("--threshold", type=float, default=0.02)
    parser.add_argument("--diffs", help="write a side-by-side PNG for each failing page here")
    args = parser.parse_args()
    if args.diffs:
        os.makedirs(args.diffs, exist_ok=True)

    failures = 0
    checked = 0
    for name in sorted(f for f in os.listdir(args.roundtrip_dir) if f.endswith(".pdf")):
        original = os.path.join(args.original_dir, name)
        trip = os.path.join(args.roundtrip_dir, name)
        if not os.path.exists(original):
            continue
        try:
            o_mu, t_mu = raster_mupdf(original, args.dpi), raster_mupdf(trip, args.dpi)
            o_pd, t_pd = raster_pdfium(original, args.dpi), raster_pdfium(trip, args.dpi)
        except Exception as e:  # a renderer refusing a file is itself a finding
            failures += 1
            print(f"{name}\tRENDER FAILED\t{type(e).__name__}: {e}")
            continue
        if len(o_mu) != len(t_mu):
            failures += 1
            print(f"{name}\tPAGE COUNT\toriginal {len(o_mu)} vs round trip {len(t_mu)}")
            continue
        for i in range(len(o_mu)):
            checked += 1
            mu, mu_size, mu_delta = differing_share(o_mu[i], t_mu[i])
            pd, pd_size, _ = differing_share(o_pd[i], t_pd[i])
            base, _, _ = differing_share(o_mu[i], o_pd[i])
            if max(mu, pd) > args.threshold or mu_size or pd_size:
                failures += 1
                print(f"{name}\tpage {i + 1}\tmupdf {mu:.3%}\tpdfium {pd:.3%}\t(mupdf vs pdfium on the original: {base:.3%})")
                if args.diffs:
                    pair = np.concatenate([o_mu[i], t_mu[i], np.stack([mu_delta] * 3, axis=2).astype(np.uint8)], axis=1) if o_mu[i].shape == t_mu[i].shape else o_mu[i]
                    Image.fromarray(pair).save(os.path.join(args.diffs, f"{os.path.splitext(name)[0]}-p{i + 1}.png"))
    print(f"{checked} pages checked, {failures} over the threshold ({args.threshold:.1%})")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
