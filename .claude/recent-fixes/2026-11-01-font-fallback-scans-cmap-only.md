# System font-fallback scan reads each face's `cmap` only (#2021)

`FontResolver.FindFamilyCoveringCodepoint` asks every installed face whether it covers a codepoint. Coverage came from
`GetFont` + `FontFileData.GetOrCreateFrom`, i.e. the whole file read and a full `OpenTypeFontface` parsed, and both are
cached process-wide (`_systemFontBytesCache`, `FontFactory.FontSourcesByKey`) - so the first uncovered character (emoji,
CJK) kept every installed font resident (~800 MB with 539 fonts).

Now an installed (non-custom) face is scanned with `FontResolver.ReadSystemCoverage`: it seeks the face's table directory
(`SfntCollection.FaceOffset`, so `.ttc` faces work without extracting them), reads only the `cmap` table and hands the bytes to
`CMapCoverage.ExtractFromTable`, which picks subtables by the same rules as `CMapTable.Read` and shares the format-4/12 merge code
with `Extract(CMapTable)`. Results are cached process-wide by (path, face index) - system files never change, ranges are tiny.

Not changed: `FacePresentationMatches` (emoji/text presentation requests) still loads a full font, but it runs only after
`FaceCovers` is true, so just the few covering faces are loaded. Custom/`@font-face` faces keep the parsed-font path.

Evidence: `CMapCoverageFromTableTests` asserts the table reader equals the parsed-font coverage for bundled BMP, astral and
subset fonts; PeachDrawing.Text.Tests (6596) and PeachPDF.Tests pass.
