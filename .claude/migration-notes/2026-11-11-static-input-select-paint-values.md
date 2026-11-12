# Static `<input>` and `<select>` now draw their content

Before: without interactive PDF forms, `<input>` and `<select>` were empty bordered boxes (no value, placeholder, checked state, selected option, button label, range thumb or color swatch; radios were square).
Now: the same drawing a screen host gets is used in a PDF - value (bullets for `password`), placeholder, check mark, round radio with dot, selected option with a dropdown arrow, button label, range, color swatch. The extracted text of such a page now contains the control text. Interactive PDF forms still draw the field's own appearance.
