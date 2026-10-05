# `text-decoration` on an `<input>` or `<select>` no longer draws a line across the control

Before: a control with `text-decoration: underline` (or overline, line-through) drew one line across its full
width, over a box with no text in it. After: no line is drawn, since the control's value is not page text.
