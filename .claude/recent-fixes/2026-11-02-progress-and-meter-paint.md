# Progress and meter are drawn

A `progress` bar and a `meter` gauge are drawn by `MeterFragmentPainter` (chosen in `FragmentContentPainters.For` for any plain box whose tag is `progress` or `meter`) and sized by a UA rule in `CssDefaults` (`inline-block`, 10em by 1em, `vertical-align: -0.2em`). Unlike the form-control painter this is **not** behind `PaintFormControls`: the decision was that a PDF shows them too, so default output changes (see the migration note).

- **Children are not painted.** A content painter replaces the generic paint, children included, so the fallback text inside the element never appears; it is still laid out inside the fixed box, which is why the size is set by CSS and not left to content.
- **Meter color is the spec algorithm, not a gradient.** The value is in one of three regions (below `low`, between, above `high`); the distance in regions from the one `optimum` is in picks green, yellow or red. A meter with no attributes is 0..1, value 0, optimum in the middle, so it is an empty green track.
- **Accent.** `accent-color: auto` gives a blue (0,117,255) rather than the text color: a black progress bar reads as a rendering bug, and browsers use their accent.
- **Verified by looking.** Both PDFium and MuPDF rasterize the showcase identically; the pixel tests (`MeterPaintTests`) count fill, track and color, and assert the fallback text is not drawn.
