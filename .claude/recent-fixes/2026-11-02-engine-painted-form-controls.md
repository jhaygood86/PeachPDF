# Form controls are drawn by the engine for a screen host, behind an option

`HtmlDocumentOptions.PaintFormControls` routes every `CssBoxFormField` (an `<input>` or `<select>`) to `ScreenFormControlPainter`, which draws the control's look from its own resolved CSS: the same border/background and glyph helpers (`FormFieldChrome`) the interactive-PDF path uses, plus text, a button label, a select's chosen option with a chevron, and a range.

- **Why an option, not the default.** A control in a PDF is a plain bordered box and has been for every release; drawing marks and text would change every existing form's output. The interactive-forms path (`CollectFormFields`) still wins when both are on, because the widget draws its own appearance and the page text would double it (see `FormFieldFragmentPainter`).
- **Dispatch order in `FragmentContentPainters.For`**: interactive field painter first, then the screen painter, then the generic path.
- **Read from the element's attributes** (`checked`, `value`, `placeholder`, `min`/`max`) rather than `FormFieldMapper.Classify`, which maps buttons, ranges and files to `None` for PDF fields; `FormFieldKind` is public and gaining kinds would change PDF classification.
- **Not done:** `progress`, `meter` (plain elements with fallback children, not `CssBoxFormField`), date/colour/file inputs, `textarea`, the caret and selection, and any interaction - toggling, typing and focus rings need the form-state API and the host input bridges, which are separate pieces.
