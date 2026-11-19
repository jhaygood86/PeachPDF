# Appearances for annotations that have none

13 of the PDFium corpus's blank pages were annotations with no `/AP`: eleven form widgets and two text-markup annotations. The importer only
drew existing appearance streams. `PdfAppearanceSynthesizer` (+ `.Widgets.cs`) now makes one and `PdfAppearance.DrawAnnotations` runs it
through the ordinary interpreter in page space. Corpus (exact fetch): 148 -> 155 pass, blanks 19 -> 12.

- **Text markup:** highlight (multiplied fill of each quad's box, yellow default), underline, strike-out, squiggly (zigzag), PDFium's rules.
- **Widgets:** `/MK` background and border (`/BS` width and style), check/radio marks by `/MK /CA` (circle, square, diamond, cross, star, check),
  text fields (`/V` in the `/DA` font and color, `/MaxLen`, `/Q`, password asterisks, multiple lines, auto font size), combo and list boxes
  (selection in PDFium's blue). Text is measured with the importer's own font loader so alignment works.

**Corpus trap: the expectations contain test-harness UI.** `pdfium_test` tints every form field (highlight 0xFFE4DD, alpha 100) and draws caret
and scroll bar chrome, and some values are typed or scripted (`password`, `bug_113910`, the second field of `bug_733528`: no `/V` in the file).
The runner draws the same tint (`Evaluator.Render`); the rest is not reproduced and those tests stay blank. See
[the accepted gap](../accepted-gaps/scripted-and-harness-form-state.md).

**Second trap: tolerance hid the missing marks.** With the tint drawn, a near-empty field page is inside the 2% pixel tolerance without any
check mark, and nine widget tests "passed" vacuously (158). `Evaluator` now calls a page blank when it keeps under a quarter of PDFium's ink
(pixels well off white; the tint is not ink). That also exposed two honest gaps that had been passing: `bug_40040570` (zero-area fills that
PDFium widens to a hairline) and `bug_440028542` (a function-based `sh` shading draws nothing).
