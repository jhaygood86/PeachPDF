# Form controls are drawn at their real border box, and editing one replaces its value

Affects documents rendered with `PdfGenerateConfig.EnableInteractivePdfForms`, and the static
rendering of `<input>`/`<select>` either way.

## Checkbox and radio controls are square

**Before:** an unstyled `<input type="checkbox">` was drawn 9.75pt wide by 16.75pt tall, and an
`<input type="radio">` as an ellipse of the same proportions. Setting equal `width` and `height` did
not help — the control stayed taller than it was wide by a fixed 7pt.

**Now:** both are square. The UA stylesheet no longer applies the text-field `padding: 1pt 2pt` to a
checkbox or radio (browsers' own UA stylesheets zero it there too), and each now carries the browser
default `margin: 3px 3px 3px 4px`, so a bare `<input type="checkbox"> Label` has room around the
control instead of sitting flush against the label text.

A document that relied on the old spacing — for example one that added `&nbsp;` to separate a
checkbox from its label — will now show slightly more space. Set `margin: 0` on the control to get the
previous flush layout back; a control with no margin sits immediately against whatever follows it,
with no gap of its own beyond any whitespace the source actually has.

## A field's border and padding are inside its drawn box

**Before:** a text or select field's drawn chrome covered only its *content* box horizontally, so
`input { width: 160pt }` produced a 160pt-wide border, while vertically its border and padding were
counted twice.

**Now:** the drawn box — and the interactive widget's own rectangle — is the field's border box, like
any other CSS box: `width` and `height` size the content, and the element's `border` and `padding` are
added around it. A field declared `width: 160pt; padding: 1pt 2pt; border-width: 0.75pt` is drawn
165.5pt wide.

## Editing a field replaces its value instead of drawing over it

**Before:** typing into a generated text or combo-box field could leave the original value visible
behind the new one, and clearing a field could leave a ghost of what had been there. Some readers also
showed the old value as mojibake alongside the new text.

**Now:** the value is enclosed in the `/Tx` marked-content sequence ISO 32000-1 §12.7.3.3 defines for
variable text, which is how a reader knows to replace exactly that region and keep the field's CSS
border and background around it. No document change is needed.
