#!/usr/bin/env python3
"""Convert the upstream Liberation TrueType fonts to WOFF for the Blazor WebAssembly demo.

The demo ships fonts as static web assets, and WOFF roughly halves the download versus TTF.

WOFF 1.0 rather than WOFF2, deliberately: WOFF2 is Brotli-compressed, and a browser/WebAssembly
host cannot decode Brotli. System.IO.Compression.Brotli is stubbed to throw
PlatformNotSupportedException there, and that is a managed-side limitation -- installing the
`wasm-tools` workload and natively relinking the runtime (which does link libbrotlidec) does not
change it, as measured. WOFF 1.0 uses zlib/deflate, which works, at about 2.3 MB against WOFF2's
1.6 MB for these twelve faces.

The conversion is *not* a free format change: the Liberation licence declares a Reserved Font
Name ("Copyright (c) 2012 Red Hat, Inc. with Reserved Font Name Liberation."), and OFL 1.1's
definition of a "Modified Version" explicitly includes changing formats.

The SIL OFL FAQ (https://openfontlicense.org/documents/OFL-FAQ.txt) section 2.2.1 permits
keeping the name only when:

    the original font data remains unchanged except for WOFF compression, and WOFF-specific
    metadata is either omitted altogether or present and includes, unaltered, the contents of
    all equivalent metadata in the original font.

and 2.2.2 warns that many conversion tools do not meet this. So this script:

  * compresses losslessly with fontTools (no subsetting -- FAQ 2.6 is explicit that removing
    glyphs is modification and forfeits the RFN),
  * emits no WOFF extended-metadata block (the simpler of the two permitted options), and
  * verifies the round trip afterwards, comparing glyph outlines, metrics, character mapping
    and the name table's copyright/licence records against the source.

Run from the repository root:

    python3 assets/fonts/convert_liberation_webfonts.py

Add --source-dir to convert from an already-extracted copy instead of downloading.
"""

from __future__ import annotations

import argparse
import io
import pathlib
import sys
import tarfile
import urllib.request

from fontTools.ttLib import TTFont

# The GitHub releases API lists no assets for this project; the built TTFs are published as a
# release *attachment*, which is what this URL points at.
SOURCE_URL = (
    "https://github.com/liberationfonts/liberation-fonts/files/7261482/"
    "liberation-fonts-ttf-2.1.5.tar.gz"
)
SOURCE_VERSION = "2.1.5"

# Liberation 2.x ships Sans, Serif and Mono only. Sans Narrow is a separate project under
# GPLv2-with-font-exception rather than the OFL, and is deliberately not bundled.
FAMILIES = ("LiberationSans", "LiberationSerif", "LiberationMono")
STYLES = ("Regular", "Bold", "Italic", "BoldItalic")
FACES = tuple(f"{family}-{style}.ttf" for family in FAMILIES for style in STYLES)

# name table IDs whose contents carry the licence: copyright notice, licence description and
# licence URL. These must survive the conversion for the redistribution to stay compliant.
LICENCE_NAME_IDS = (0, 13, 14)


def download_sources(destination: pathlib.Path) -> None:
    """Fetch and extract the upstream TTFs (plus LICENSE/AUTHORS) into destination."""
    print(f"Downloading {SOURCE_URL}")
    with urllib.request.urlopen(SOURCE_URL) as response:
        payload = response.read()

    destination.mkdir(parents=True, exist_ok=True)
    with tarfile.open(fileobj=io.BytesIO(payload), mode="r:gz") as archive:
        for member in archive.getmembers():
            name = pathlib.PurePosixPath(member.name).name
            if not member.isfile() or name not in FACES + ("LICENSE", "AUTHORS"):
                continue
            extracted = archive.extractfile(member)
            assert extracted is not None
            (destination / name).write_bytes(extracted.read())

    print(f"Extracted {len(FACES)} faces and the licence to {destination}")


def verify_round_trip(source_ttf: pathlib.Path, woff: pathlib.Path) -> None:
    """Fail loudly unless the WOFF carries exactly the source font's data.

    fontTools recompresses each table, so the raw stored bytes are not
    expected to be identical -- the font *data* is what has to match, which is what this
    compares.
    """
    original = TTFont(source_ttf)
    converted = TTFont(woff)

    flavor_data = converted.flavorData
    if flavor_data is not None and getattr(flavor_data, "metaData", None):
        raise SystemExit(f"{woff.name}: unexpected WOFF metadata block")

    if original.getGlyphOrder() != converted.getGlyphOrder():
        raise SystemExit(f"{woff.name}: glyph order changed")

    original_glyf, converted_glyf = original["glyf"], converted["glyf"]
    for glyph_name in original.getGlyphOrder():
        before = original_glyf[glyph_name]
        after = converted_glyf[glyph_name]
        before.expand(original_glyf)
        after.expand(converted_glyf)
        if before.compile(original_glyf) != after.compile(converted_glyf):
            raise SystemExit(f"{woff.name}: outline changed for glyph {glyph_name!r}")

    if original["hmtx"].metrics != converted["hmtx"].metrics:
        raise SystemExit(f"{woff.name}: horizontal metrics changed")

    if original.getBestCmap() != converted.getBestCmap():
        raise SystemExit(f"{woff.name}: character mapping changed")

    for name_id in LICENCE_NAME_IDS:
        before = {
            (r.platformID, r.platEncID, r.langID): r.toUnicode()
            for r in original["name"].names
            if r.nameID == name_id
        }
        after = {
            (r.platformID, r.platEncID, r.langID): r.toUnicode()
            for r in converted["name"].names
            if r.nameID == name_id
        }
        if before != after:
            raise SystemExit(f"{woff.name}: name record {name_id} changed")
        if not before:
            raise SystemExit(f"{woff.name}: name record {name_id} missing from the source")

    original.close()
    converted.close()


def main() -> int:
    here = pathlib.Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--source-dir",
        type=pathlib.Path,
        help="Directory holding the upstream .ttf files; downloaded when omitted.",
    )
    parser.add_argument(
        "--output-dir",
        type=pathlib.Path,
        default=here,
        help="Where to write the .woff files (default: this script's directory).",
    )
    args = parser.parse_args()

    source_dir = args.source_dir
    if source_dir is None:
        source_dir = here / ".liberation-src"
        download_sources(source_dir)

    args.output_dir.mkdir(parents=True, exist_ok=True)

    total_ttf = total_woff = 0
    for face in FACES:
        source = source_dir / face
        if not source.is_file():
            raise SystemExit(f"Missing source font: {source}")

        target = args.output_dir / (source.stem + ".woff")
        font = TTFont(source)
        font.flavor = "woff"          # zlib/deflate per table; no data is altered
        font.save(target)
        font.close()
        verify_round_trip(source, target)

        total_ttf += source.stat().st_size
        total_woff += target.stat().st_size
        print(f"  {face} -> {target.name} ({source.stat().st_size:,} -> {target.stat().st_size:,} bytes)")

    licence = source_dir / "LICENSE"
    if licence.is_file():
        (args.output_dir / "LiberationFonts.LICENSE.txt").write_bytes(licence.read_bytes())
        print("  LICENSE -> LiberationFonts.LICENSE.txt")

    print(
        f"\nConverted {len(FACES)} Liberation {SOURCE_VERSION} faces: "
        f"{total_ttf:,} -> {total_woff:,} bytes "
        f"({100 * total_woff / total_ttf:.0f}% of the original)."
    )
    print("Round trip verified: outlines, metrics, cmap and licence name records unchanged.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
