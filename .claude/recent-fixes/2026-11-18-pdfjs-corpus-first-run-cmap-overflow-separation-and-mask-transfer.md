# The pdf.js corpus's first run: 755 of 1,073 pass; four importer defects found

Windows, pdf.js at the commit in `pdfjs/PDFJS_COMMIT`, every page of every PDF up to 3 MB compared with PDFium (pypdfium2 5.12.1) at 1 px/pt.
First run: 755 pass, 303 fail, 15 blank, 22 skipped (10 need a password, 12 PDFium could not render), 126 s. After the fixes below: 773 pass, floor 730.

- **`OverflowException` in the predefined CMap reader** (6 files: issue15053, issue19182, issue8372, mixedfonts, ...). `PdfCMapBinary.Reader.Zigzag()`
  read through `Varint()`, which casts to `int` with `checked`, but a zigzag-coded difference of two 32-bit codes needs 33 bits, and a four-byte
  codespace bound (up to 0xFFFFFFFF) and a range span were read as `int` too. Every file using such a CMap (the UTF-32 ones) failed to open its page.
  `Varint64()` now backs the zigzag, codespace and span reads. The PDFium pixel corpus has no file using them, which is why it was never seen.
- **Separation and DeviceN were not color spaces** (colorspace_atan/cos/sin, devicen, type4psfunc and eight more: +12). `cs` failed and the color
  stayed black. `PdfColorSpace.Tint` evaluates the tint transform into the alternate space, treats `/None` as marking nothing, starts at full tint,
  and caches colors (one to four colorants, eight bits each) because an image repeats few of them.
- **A soft mask's `/TR` was ignored** (issue22489, smask_*_oob_transfer, smask_alpha_oob: +4). An inverting transfer function swapped what showed.
  It is now a 256-entry table over the mask tile (luminosity, or alpha for an alpha mask), clamped to 0..1.
- **The oracle itself:** `oracle` must not be left running while the runner is rebuilt (its own process holds the DLL), and a PDF whose tiling
  pattern has tiny steps can take PDFium 15 minutes, which the oracle's timeout turns into an error file.

Triage of the rest (not yet addressed): 68 failures are font substitution only (PDFium draws its own fonts); 12 files use overprint (`gs OP`/`op`,
reported only); 12 use a Type0 font that is not embedded and has no ToUnicode map; 6 are RC4 (a policy, see the memory of why); 7 report a malformed
`Tf` or a missing font; 4 report a `shading colour space`; 3 have no page tree to repair; 2 time out at 120 s.
