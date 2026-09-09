#!/usr/bin/env python3
"""
Generates CcmpLigatureTest.ttf - the fixture for GSUB `ccmp` application and
default-ignorable handling.

It is deliberately shaped like a modern color-emoji font in the two ways that
matter to those code paths, and unlike almost every Latin text font:

  * its ONLY GSUB feature is `ccmp` - there is no `liga`/`rlig`/`clig` at all,
    so a shaper that treats `ccmp` as opt-in produces no substitution whatsoever
    (this is exactly how Noto Color Emoji is built, and why every non-ZWJ emoji
    sequence used to render unligated);
  * it has NO cmap entry for U+FE0F (VARIATION SELECTOR-16), so that codepoint
    resolves to .notdef - which is correct and normal, since a variation
    selector is a Default_Ignorable_Code_Point that no font is expected to draw.

Glyph inventory (all plain rectangles - only glyph identity is ever asserted,
never the outline itself):

  A     U+0041   left component
  B     U+0042   right component
  AB    (no cmap) the ligature A+B produces via `ccmp`
  S     U+0053   a standalone glyph, used to check a lone ignorable

The `ccmp` feature holds one Lookup Type 4 (Ligature) subtable: A + B -> AB.
Note the ligature's component list is just [B] after coverage glyph A - it never
mentions U+FE0F, so "A U+FE0F B" only ligates if the shaper steps over the
hidden ignorable while matching components (HarfBuzz's SKIP_MAYBE).

Original, hand-authored fixture (no third-party font data), released into the
public domain - see CcmpLigatureTest.LICENSE.txt.

Regenerate with:  python3 generate_ccmp_ligature_font.py
Requires: fonttools
"""
import os

from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import newTable
from fontTools.ttLib.tables import otTables as ot

UPEM = 1000
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "CcmpLigatureTest.ttf")

# glyph name -> (cmap codepoint or None, advance width, rectangle width)
GLYPHS = {
    "A": (0x0041, 600, 500),
    "B": (0x0042, 600, 400),
    "AB": (None, 1200, 1100),
    "S": (0x0053, 600, 300),
}


def rect_glyph(width):
    pen = TTGlyphPen(None)
    pen.moveTo((50, 0))
    pen.lineTo((50 + width, 0))
    pen.lineTo((50 + width, 700))
    pen.lineTo((50, 700))
    pen.closePath()
    return pen.glyph()


def build_gsub(fb):
    """One `ccmp` feature -> one Type 4 ligature lookup: A + B -> AB."""
    lig = ot.Ligature()
    lig.Component = ["B"]          # components AFTER the coverage-matched first glyph
    lig.CompCount = 2
    lig.LigGlyph = "AB"

    lig_set = ot.LigatureSet()
    lig_set.Ligature = [lig]
    lig_set.LigatureCount = 1

    subtable = ot.LigatureSubst()
    subtable.Format = 1
    subtable.ligatures = {"A": [lig]}

    lookup = ot.Lookup()
    lookup.LookupType = 4
    lookup.LookupFlag = 0
    lookup.SubTable = [subtable]
    lookup.SubTableCount = 1

    lookup_list = ot.LookupList()
    lookup_list.Lookup = [lookup]
    lookup_list.LookupCount = 1

    feature = ot.Feature()
    feature.LookupListIndex = [0]
    feature.LookupCount = 1
    feature.FeatureParams = None

    feature_record = ot.FeatureRecord()
    feature_record.FeatureTag = "ccmp"
    feature_record.Feature = feature

    feature_list = ot.FeatureList()
    feature_list.FeatureRecord = [feature_record]
    feature_list.FeatureCount = 1

    lang_sys = ot.LangSys()
    lang_sys.ReqFeatureIndex = 0xFFFF
    lang_sys.FeatureIndex = [0]
    lang_sys.FeatureCount = 1
    lang_sys.LookupOrder = None

    script = ot.Script()
    script.DefaultLangSys = lang_sys
    script.LangSysRecord = []
    script.LangSysCount = 0

    script_records = []
    for tag in ("DFLT", "latn"):
        record = ot.ScriptRecord()
        record.ScriptTag = tag
        record.Script = script
        script_records.append(record)

    script_list = ot.ScriptList()
    script_list.ScriptRecord = script_records
    script_list.ScriptCount = len(script_records)

    gsub = ot.GSUB()
    gsub.Version = 0x00010000
    gsub.ScriptList = script_list
    gsub.FeatureList = feature_list
    gsub.LookupList = lookup_list

    table = newTable("GSUB")
    table.table = gsub
    fb.font["GSUB"] = table


def main():
    order = [".notdef"] + list(GLYPHS)
    fb = FontBuilder(UPEM, isTTF=True)
    fb.setupGlyphOrder(order)

    # U+FE0F is deliberately absent - see this script's own docstring.
    cmap = {cp: name for name, (cp, _, _) in GLYPHS.items() if cp is not None}
    fb.setupCharacterMap(cmap)

    pen = TTGlyphPen(None)
    glyphs = {".notdef": pen.glyph()}
    metrics = {".notdef": (600, 50)}
    for name, (_, advance, width) in GLYPHS.items():
        glyphs[name] = rect_glyph(width)
        metrics[name] = (advance, 50)

    fb.setupGlyf(glyphs)
    fb.setupHorizontalMetrics(metrics)
    fb.setupHorizontalHeader(ascent=800, descent=-200)
    fb.setupNameTable({
        "familyName": "Ccmp Ligature Test",
        "styleName": "Regular",
        "psName": "CcmpLigatureTest-Regular",
    })
    fb.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
    fb.setupPost()
    build_gsub(fb)

    fb.save(OUT)
    print("wrote", OUT)


if __name__ == "__main__":
    main()
