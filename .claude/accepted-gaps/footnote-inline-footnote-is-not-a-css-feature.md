# `float: inline-footnote` is a PrinceXML extension, not a CSS feature

This file replaces an earlier one that recorded "css-gcpm-3 defines `float: footnote` alongside
`float: inline-footnote`" as a gap. **That premise was wrong**, checked against the specs rather than
assumed:

- The current [css-gcpm-3 editor's draft](https://drafts.csswg.org/css-gcpm-3/) defines exactly one
  footnote `float` value, `footnote`. So does the
  [2014 Working Draft](https://www.w3.org/TR/2014/WD-css-gcpm-3-20140513/). `inline-footnote` appears
  in neither, at any revision.
- It is a **PrinceXML** extension, and its actual meaning is not the one the old note recorded
  ("rendered inline, immediately after the paragraph"). Per Prince's own documentation it moves the
  footnote *marker* into the footnote box as its first inline box, in contrast to Prince's default of
  hanging the marker outside in the left margin - i.e. it is equivalent to
  `float: footnote` plus `footnote-style-position: inside`, another Prince-only property.
- PeachPDF already places `::footnote-marker` as the first inline box of the footnote body (the UA
  stylesheet's `list-style-position: inside`, matching css-gcpm-3's own default sheet), so the
  behaviour `inline-footnote` selects in Prince is already PeachPDF's only behaviour. Implementing
  the keyword would be a pure synonym for `float: footnote`.

It is therefore **not implemented, and not tracked as a gap to close**: PeachPDF implements official
CSS, and there is no CSS here to implement. `footnote-style-position` is out for the same reason.

The other half of the original note - column-scoped footnote areas - **is** closed: css-gcpm-3 leaves
it as an open issue pointing at page floats, and PeachPDF now implements it through the
standards-track [css-page-floats](https://drafts.csswg.org/css-page-floats/) `float-reference: column`
(see [float-reference-is-inert-outside-a-footnote.md](float-reference-is-inert-outside-a-footnote.md)
for what that property does *not* do).
