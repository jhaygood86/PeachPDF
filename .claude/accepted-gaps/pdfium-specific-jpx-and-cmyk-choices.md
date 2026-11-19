# Where PDFium's JPX and CMYK output is a choice, not the specification

Four PDFium corpus tests stay failing on purpose:

- **DeviceCMYK conversion** (`jpxdecode`, `jpxdecode_without_bitspercomponent`, `jpxdecode_without_colorspace`, `jpxdecode_without_smaskindata`,
  `bug_42270732`): PDFium converts CMYK to sRGB through an Adobe-style table (pure cyan is about 0,174,239); this importer uses the plain
  formula (0,255,255) when it has to convert at all, and a PDF canvas keeps CMYK as CMYK, so a viewer's own conversion applies. The PDF
  specification does not define the device conversion; matching one viewer's table would need a large embedded table.
- **`jpxdecode_with_mismatch_colorspace`:** a JPX whose component count contradicts the dictionary's /ColorSpace (RGB data with a CMYK
  space...). PDFium draws nothing; this reader draws the codestream with its own color. The specification calls the dictionary entry
  authoritative but not what to do on a contradiction.
- **`bug_557223`:** a codestream whose header says 720x243 inside a 32x32 image dictionary, a fuzzing artifact.
