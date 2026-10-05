# A form control draws no text-decoration line (#1616)

An `<input>`/`<select>` with `text-decoration: underline` drew one line across its whole width. The issue
proposed collecting the control's own word as a span, so the line would sit under the text only. Running
it showed the premise is off: the control's value is never page content in this engine. Its
`CssRectFormField` word is a phantom that only gives the control a size, and the text a reader shows
comes from the widget's appearance stream (see `FormFieldFragmentPainter`). Rasterized, the control is an
empty box, so the line was a stray rule over nothing and there is no text to underline.

Fix: `FragmentPainter` skips the decoration for a `CssBoxFormField` instead of sending it down the
per-own-line path. The two tests that pinned the old control-wide line now assert no line is drawn
(underline, overline, line-through, placeholder, explicit width, select).

If a control's value ever becomes drawn page text, the decoration should come back under that text:
give the root fragment's own words a span in `DecorationContent.Of` rather than reviving the
whole-control line.

Evidence: rendered through the CLI and rasterized before the change (empty bordered box with a line).
