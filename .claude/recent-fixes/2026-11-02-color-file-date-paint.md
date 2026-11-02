# Colour, file and date/time inputs are drawn

`ScreenFormControlPainter` (the `PaintFormControls` look, so a host view only - a PDF is unchanged) draws three more kinds of `<input>` that used to fall into the generic text branch and show their raw `value`: a colour input as a block of its colour (`ColorOf`: `#rrggbb` or `#rgb`, black when there is none or it is not hex), a file input as a grey "Choose File" button with "No file chosen" beside it, and a date, time, datetime-local, month or week input with no value as the pattern its value takes, in the placeholder colour (`DateTimeHint`). One with a value still shows the value.

- **No picker, no chosen file.** A host has to supply a platform picker and write the result back (`SetValue`); until it does the file input always says no file is chosen, and the colour and date inputs show only what their attribute holds.
- **Not edited as text.** `EditableField` only knows the text-like types, so none of these takes the caret or typed characters.
- **Pixel tests, not a screenshot.** `FormControlPaintTests` counts ink in the swatch, the button and the pattern; the helpers have unit tests in the engine test project.
