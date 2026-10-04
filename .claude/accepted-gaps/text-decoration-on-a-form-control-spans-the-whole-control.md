# An underlined form control gets a line across the whole control

An `<input>` or `<select>` with `text-decoration: underline` draws one line across the control's full
width; a browser underlines only the control's text (css-text-decor-3 section 2: a decoration lands on
the box's inline content). A `<textarea>` is not affected - it is a static `inline-block`, not a form
field - and is underlined under its text (`TextareaUnderline_IsUnderItsTextNotTheWholeControl`).

An `<input>` or `<select>` is a replaced atomic inline whose text is its own `CssRectFormField` word on
the root fragment, not an inline child. `DecorationContent.Of` collects spans only from *children*, so
`FragmentPainter`'s propagated path finds nothing for it and would draw nothing. The
`box is not CssBoxFormField` guard in `PaintBoxContent` therefore leaves it on the older per-own-line
path, which draws the too-wide line.

`FormFieldUnderline_KeepsItsControlWideLine` and `SelectUnderline_KeepsItsControlWideLine` pin that, and
are what to change if the control's text becomes collectable inline content (give the root's own words a
span, or emit the word as a child).

Documented in the `text-decoration-line` row of `docs/html-css-support.md`. Tracking issue:
[#1616](https://github.com/jhaygood86/PeachPDF/issues/1616).
