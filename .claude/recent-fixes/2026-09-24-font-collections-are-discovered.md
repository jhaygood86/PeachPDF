# System font discovery reads `.ttc`/`.otc` collections

`FontResolver` globbed `*.ttf` and `*.otf` only, so every font a platform ships inside a collection was invisible
- Windows' Cambria Math and MS Gothic, macOS's Helvetica/Times/Menlo/Courier (which is why the macOS column of the
generic-family table named fonts that were then "not installed" and fell back to the default), the Noto CJK fonts on
Linux. It surfaced as the `math` generic's Windows chain being dead.

**The load-bearing decision: a face is extracted, not addressed.** Everything downstream - `XFontSource`, the
OpenType table readers, subsetting, embedding, checksums - reads *one standalone sfnt's bytes*. Teaching each of
them that table offsets in a collection are absolute from the file start (and that the directory isn't at 0) would
touch dozens of readers. `FontCollection.ExtractFace` instead rebuilds one face as an ordinary font (fresh
directory, that face's tables copied, checksums carried over) the first time `GetFont` is asked for it, and the
result is cached like any other system font's bytes. A face of a collection is, past that point, indistinguishable
from a `.ttf`. Only the face's own tables are read from disk, so a many-faced CJK collection costs one face.

**Identity:** `_systemFontPaths` now maps a face name to `(path, faceIndex)`; the face name is still the font's own
full name, unique per face. `ParseSystemFonts` lists a collection's faces individually
(`TtfFontDescription.LoadDescriptions`), skipping one unreadable face rather than the whole file, and
`DefaultFontResolver.GetInstalledFontFamilyNames` enumerates every face too (it used to read face 0's family only).
`TtfFontDescription.LoadDescription(stream)` on a collection is face 0, so `SupportedFonts[0]`-style callers keep
working; collections are globbed *last* so that first entry is still an ordinary font where one exists.

**`AddFontFromStream` given a collection registers face 0** (extracted the same way) rather than failing to parse.

**Bounds:** the face count, table count and every table's offset+length are validated against the file before any
allocation is sized from them (`FontCollection`), because a collection header is untrusted input.

**The macOS CI fallout was exactly that class, and it was 8 tests.** macOS's `sans-serif` maps to Helvetica, which lives in
`Helvetica.ttc`; before this change it was "not installed" and fell back to Arial, now it resolves for real. Helvetica's
ascent + descent is exactly 1em (Arial's is 1.149em) and its widths differ, so fixtures written as `font: 12px sans-serif`
and calibrated to Arial's metrics flipped: `LineHeightLineBoxExtentTests` (its own guard reported "natural line height (9pt)
exceeds the declared 9pt" - a 12px font whose line box is exactly 12px), `FloatContainmentTests`, and
`FootnoteColumnScopeIntegrationTests` (column band arithmetic). Those three files now call `BundledFonts.PinSansSerifAsync`, which
maps `sans-serif` to the bundled Liberation Sans (Arial's metrics, the ones the fixtures were written against). Pinning to
Source Sans 3 instead broke other tests in the same files (their wrap widths are Arial-calibrated) - the pin has to match
the calibration font, not merely be a fixed one. Expect the same shape wherever else a fixture says `sans-serif`/`serif`/
`monospace` and asserts a metric-dependent number.

**Evidence:** tests build collections from two bundled fonts (`SyntheticFontCollection`, sharing identical tables the way
a real one does) and assert the extracted face maps characters to the same glyphs through the real table parser; a
Windows-only test asserts Cambria Math is found in the real `cambria.ttc` with a MATH table and that `math` resolves
to it.
