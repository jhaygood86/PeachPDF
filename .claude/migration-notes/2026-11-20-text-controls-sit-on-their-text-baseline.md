# A text entry or drop-down sits on its line of text, and no longer makes its line taller than itself

**Before (0.10.0):** an `<input>` (text, search, tel, url, email, password, number) or a drop-down `<select>` was aligned like an image: its bottom margin edge
sat on the line's baseline. A label beside it (`<label style="display:inline-block">`) therefore put its text on the control's bottom edge, a good 8pt below the
text inside the control, where a browser shows the two on one baseline. The line was also taller than the control, because the strut's descent hung under it and
the control's own bottom border and padding were added to the line a second time.

**Now:** HTML renders these controls as an inline-block depicting a one-line text control, and CSS 2.1 section 10.8.1 puts an inline-block's baseline on its last
line box, so the baseline is the control's line of text (centered in its content box). A label beside the control shares that baseline, and a line holding only
such controls and text that fits above and below the baseline is exactly as tall as the control. Checkboxes, radio buttons, buttons, pickers, list boxes and
`<textarea>` keep resting on their bottom margin edge. A control that is a block of its own (positioned, or `display: block`) is unaffected.

Documents that lay a form out with inline labels and inputs get shorter rows, and the labels move up to meet the text in the field.
