# `<textarea>` as a control (stage one: the representation and the look)

First of a series that makes `textarea` editable in every host. This stage only changes what it is and how it is drawn; editing follows.

- `CssBox.CreateBox` makes a `textarea` a `CssBoxFormField`. `DomParser.SeedTextAreaValues` copies its text content into the `value` attribute (one
  leading line break dropped, CRLF folded) *before* `ApplyAttributeOverrides`, so a host's `SetValue` override still wins and everything that already
  reads/edits `value` (`GetFormState`, `SetValue`, the form editor, annotations) works unchanged. The text children stay in the tree but a form-field box
  never measures its children, so they are never flowed or painted.
- `TextAreaLayout` (Paint/Content) is the one line breaker - at line breaks, after spaces, inside an over-long word - plus position/point arithmetic
  (`LineOf`, `OffsetOf`, `IndexAt`). The screen look, the PDF appearance and, next, the editing model must all use it so a caret, a click and a drawn
  line agree.
- Three painters draw the text: `ScreenFormControlPainter` (host look, with selection/composition/caret), `FormFieldAppearance` (the PDF widget's
  appearance), and `StaticTextAreaPainter` for a page that is neither - it calls the generic box paint first, so `box-shadow`/outline still work
  (the first attempt reused the screen painter and dropped `:read-write`'s inset shadow, caught by `FormStateSelectorTests`).
- Found by rasterizing, not by tests: with the UA sheet unchanged the box had no border at all, and pdfium draws widget text only after
  `init_forms()` + `may_draw_forms=True` (mupdf always does). Both renderers agree on the final output, interactive and static.
- `FormFieldClassification.Multiline` is new public API (snapshot updated); `FormFieldBuilder` passes it to the writer, which already had the flag.
- Editing model (same PR, because the selection/composition/caret painting needed it to be reachable and tested): `EditableField` accepts a textarea,
  `ReplaceEditText` keeps line breaks (as line feeds) for it, `GetEditPlace` puts the caret on its line, and new public `GetEditIndexAt(x, y)`,
  `GetEditIndexByLine` (Up/Down, same column, first/last line go to the start/end) and `GetEditLineRange` (Home/End) take the line breaker's view.
- Trap: `StyleUpdater.DirtyRegionsOf` is wrong for a textarea - it unions the lines and words of everything inside the box (its unmeasured text
  children sit at the origin) and inflates the result, so the region started at (-1.25, -2.25) and was far too narrow, which made every character
  wrap onto its own line. A textarea's geometry comes from `StyleUpdater.BoxRegionOf` (the box's own `PrimaryRect`) instead.
- Not done: host key handling (Up/Down/Home/End/Enter calls) and the TSF/InputConnection bridges for it, scrolling a value taller than the box, host
  text-input bridges for it, and `resize`. `docs/accessibility.md` says "drawn but not edited yet".
