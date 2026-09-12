# Form controls: the HTML attributes that map to PDF field entries, and `placeholder`

Follow-on to [the geometry/marked-content fix](2026-09-11-form-field-square-geometry-and-variable-text-marked-content.md),
prompted by asking what `type=number`/`date` and `placeholder` actually did. The answer, measured on a
generated PDF rather than read off the code: `ClassifyAuto` already collapses every text-like input
type to a text field, so those work — but the **only** `/Ff` bits PeachPDF ever set were the two its
own CSS extensions drive (`Comb`, `DoNotScroll`). Every HTML attribute was dropped.

`type=password` was the one that mattered: the field's `value` was drawn legibly into the appearance
stream and the reader was never told to mask typing.

## What now maps

| HTML | PDF |
|---|---|
| `readonly` | `/Ff` bit 1 ReadOnly |
| `disabled` | `/Ff` bits 1 + 3 (ReadOnly + NoExport) — HTML defines a disabled control as neither editable nor submitted, and PDF spells those separately |
| `required` | `/Ff` bit 2 Required |
| `maxlength` | `/MaxLen` |
| `type=password` | `/Ff` bit 14 Password, plus asterisks in the drawn appearance |
| `placeholder` | `/TU`, and drawn hint text by default |

Read once per box into a `FormFieldAttributes` struct on `FormFieldClassification`, so the three
Table 221 bits reach *every* field kind rather than only text. Two traps that cost a compile each:
`PdfComboBoxField` and `PdfRadioButtonField` set their own `Combo`/`Radio` bit in their constructors,
so the shared flags have to be OR'd in, never assigned over.

## Decisions worth not re-litigating

- **Password masking is presentation only — `/V` keeps the real value.** The author wrote it into the
  element's `value`, and the field genuinely is prefilled with it. ISO 32000-1 Table 228's "the value
  shall not be stored in the file when the form is saved" is an instruction to a *reader saving a
  filled form*, not to a generator prefilling one. Documented so nobody puts a secret in the HTML
  expecting it to be stripped.
- **`*` rather than `•` for the mask.** Every font has it and it is what Acrobat's own echo uses; a
  bullet would depend on the embedded subset's coverage.
- **Comb beats `maxlength`.** A comb field's cell count IS its `/MaxLen` (Table 228), so honouring a
  larger `maxlength` would let the user type past the last drawn cell.
- **The standard `placeholder` attribute is the only switch.** A second proprietary
  `-peachpdf-pdf-form-field-placeholder` property made unchanged HTML render differently from a
  browser and gave authors a second name to learn for behavior HTML already defines, so it was
  removed. `/V` stays empty, and the `/Tx BMC` fix puts the automatically drawn hint inside the
  replaceable region so a reader wipes it on the first keystroke instead of leaving it behind the
  typed text. `/TU` remains unconditional.
- **Placeholder styling uses the standard selectors.** `::placeholder` resolves through the normal
  cascade into a detached style box (never inserted into layout) and supplies the hint's color,
  opacity and font. The UA default is opaque `#7f7f7f`, so an unstyled placeholder remains PDF/A-1
  safe. If an author explicitly supplies translucent `color` or `opacity`, the ordinary PDF fill-
  alpha path and `PdfATransparencyGuard` apply rather than silently ignoring it. The separately
  implemented `:placeholder-shown` matches the empty source state at generation time and can style
  the field itself; PDF has no live CSS state after the user edits it.

## Not a defect, confirmed by the reporter's own device

**Adobe draws a red outline around every `required` field and keeps it there after it is filled in.**
This was reported as a bug and is not one: it is the reader's "Required Fields Highlight Color"
preference (Preferences → Forms) marking the field as required, tied to "Highlight Existing Fields" —
not a validation state about the current value, and under each viewer's control rather than the
document's. A diagnostic PDF with a *prefilled* required field settled it: still red. Documented in
docs/html-css-support.md so it is not "fixed" later.

## Still not mapped, deliberately

`type=number`/`date` formatting and `min`/`max`/`step` validation need Acrobat's `/AA` JavaScript
convention (`AFNumber_Format`/`AFDate_Format` and friends). That is executable script embedded in the
document, unsupported by some readers and rejected by some PDF/A profiles, so it was scoped out
rather than made an invisible default. `<textarea>` (which would want `/Ff` bit 13 Multiline) remains
out of scope for the same reason it always was — it is not a `CssBoxFormField` at all.

## What the post-change review pass caught

Worth recording, because three of these were introduced *by* the change and none broke a test.

- **A form field gained a phantom trailing word-space.** `CssRect.ActualWordSpacing` adds a whole
  space's width for any `IsImage` word, so making `CssRectFormField.IsImage` true silently inserted
  one between a checkbox and the label after it - measured at 6.5977pt for 16px monospace, and
  `margin: 0` could not remove it. Fixed by splitting the question: `CssRect.ReservesTrailingSpace`
  (virtual, defaults to `IsImage`) is overridden false for a field. Measured after: gap 0 with
  `margin: 0`, and exactly one space plus the UA margin otherwise, which is what a browser does.
  **Images still over-reserve** (`<img> X` gets two spaces where a browser gives one) - pre-existing,
  untouched here, and not covered by any accepted-gap file.
- **The `/Tx` sequence was skipped when a field had no drawable content box.** An early return on a
  degenerate `contentRect` sat *above* `BeginVariableText`, so a field whose padding and border
  consume its box got no replaceable region - the exact append-instead-of-replace bug the sequence
  exists to prevent, in the case hardest to notice. The guard now wraps only the drawing.
- **A radio group took its shared flags from whichever button was parsed first.** The HTML Standard
  makes a group required if *any* member is, so `required` on the second button was dropped. The
  flags now accumulate onto the existing group.
- The `RadioGroup` test masked `/Ff` down to `Required`, so losing the `Radio` bit (bit 16 - without
  it a `/FT /Btn` group reads as checkboxes and mutual exclusion is gone) would have passed. It now
  asserts the whole value, like its `Select` twin already did.
- The first placeholder color implementation returned a half-alpha fill, which tripped
  `PdfATransparencyGuard` for every unstyled hint. The default now comes from the UA
  `input::placeholder` rule as an opaque color; only author-requested alpha uses transparency.
- Also: `maxlength` parsed with culture-sensitive defaults (now `NumberStyles.None` + invariant), a
  paywalled `iso.org` link and a dead anchor in `docs/**`, and 23 files given a spurious UTF-8 BOM by
  the editing script.

## Evidence

- Full suite 10 822 passed / 0 failed (net8.0); generator suite 119; CLI suite 96; solution rebuild 0 warnings.
- 100% diff coverage on every changed library line.
- The `interactive_pdf_forms` showcase gained an attributes section and a drawn-placeholder section,
  re-rendered through MuPDF and PDFium.
- Verified in Adobe Reader on Android by the reporter: masking, read-only, maxlength and the
  disappearing hint all behave.
