# Form-control HTML attributes now reach the generated PDF field

Affects documents rendered with `PdfGenerateConfig.EnableInteractivePdfForms`.

**Before:** the only field flags PeachPDF ever set were the two its own CSS extensions drive
(`-peachpdf-pdf-form-field-comb` and `-peachpdf-pdf-form-field-do-not-scroll`). `readonly`,
`disabled`, `required`, `maxlength`, `placeholder` and `type="password"` were all read off the
element for nothing — a password field was an ordinary text field whose value was drawn legibly onto
the page and whose typing the reader echoed in clear text.

**Now:** each maps to its PDF equivalent — `readonly` to `/Ff` ReadOnly, `disabled` to ReadOnly plus
NoExport, `required` to Required, `maxlength` to `/MaxLen`, `type="password"` to the Password flag
with asterisks drawn in place of the value, and `placeholder` to the field's `/TU` tooltip.

Documents already using these attributes will see their generated fields change behaviour in a
reader. Two worth knowing about:

- A `required` field is outlined in red by Adobe Acrobat and Reader, and **stays outlined after it is
  filled in** — that is the reader's own required-field highlight preference, not a validation error.
  Remove `required` from any control that was carrying it decoratively.
- A `disabled` control is no longer submitted with the form, matching what HTML already said about it.

A new opt-in property, `-peachpdf-pdf-form-field-placeholder: auto`, additionally draws a field's
`placeholder` as greyed hint text when the field has no value of its own. It is `none` by default, so
no existing document changes appearance because of it.
