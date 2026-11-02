# A `<textarea>` is a form control, not a block of text

Before, a `<textarea>` was an ordinary `inline-block` whose text flowed inside it like a paragraph, with no default border, background or font, and
it never became an interactive PDF field. Now it is a form control like `<input>`:

- Its text is its value. It is drawn as lines broken for the box's width (and clipped to the box), in a PDF as on a screen, and an empty one shows its
  `placeholder`. Text styling that used to reach the flowed text through the element (for example `text-decoration`) no longer does.
- Its size comes from `cols` (default 20) and `rows` (default 2) when CSS sets none, where it used to be as wide as its container allowed or as tall
  as its text.
- The UA stylesheet gives it a thin black border, a white background, a little padding and a monospace font, as browsers do.
- With interactive PDF forms on, it becomes a fillable multi-line text field (`/Tx` with the multiline flag); `-peachpdf-pdf-form-field: none` opts out.
