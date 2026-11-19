# Zero-area fills, link borders, and two runner rules found along the way

PDFium corpus (exact fetch): 157 -> 164 of 207, blanks 12 -> 5.

- **A fill with no area is a hairline.** `bug_1296`, `bug_1639_1`, `bug_40040570` fill a line or a sliver narrower than a quarter of a pixel
  (mean width = 2 x area / perimeter over the flattened contours). By the letter of the specification that paints nothing; viewers draw a
  one-pixel line in the fill color, so `TryFillThinAsHairline` does on a raster canvas and for a plain color. A PDF canvas (`PdfRenderContext`)
  keeps the file's paths. A half-unit rule is an ordinary fill and is not touched. **First attempt, discarded:** widening every thin
  *stroke* to one pixel changed nothing in the corpus (these files stroke nothing) so it was reverted.
- **Link borders** (PDF 12.5.2) are now drawn by the appearance synthesizer: `/C` color, `/Border` or `/BS` width and dash, one-point black when
  neither is given, nothing at zero width. This makes links that give no border draw a black frame, as the specification and PDFium do.
- **Runner: read-only fields are not tinted** (`bug_1304714`: three `/Ff 1` widgets with their own appearance; the tint hid their colors), and
  **the ink rule ignores pages where PDFium drew under 16 pixels** (one dot in a corner is not content; it had turned `bug_440028542` into a
  false "blank").
- **Tried and reverted:** a finer or fold-aware patch tessellation for `shade-tensor` (no gain). See the accepted gap
  [pdfium-folded-coons-patches-and-cmyk-in-shade-tensor.md](../accepted-gaps/pdfium-folded-coons-patches-and-cmyk-in-shade-tensor.md).

Still blank: `password`, `bug_113910`, `combobox_form`, `bug_42271472` (scripts or the harness's form UI, see
[scripted-and-harness-form-state.md](../accepted-gaps/scripted-and-harness-form-state.md)) and `bug_1308_1` (a Type0 font that is not embedded and has
no ToUnicode map: nothing to stand in for it).
