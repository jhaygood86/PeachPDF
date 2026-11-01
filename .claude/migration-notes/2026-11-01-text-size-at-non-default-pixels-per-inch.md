# Text size no longer shrinks at a non-default `PixelsPerInch` / layout scale

Before: with `PdfGenerateConfig.PixelsPerInch` other than 72 (or a render context whose `PixelsPerPoint` is not 1), body text, SVG
text and `background-clip: text` shapes were drawn at 1/scale of their CSS size while box geometry was correct (e.g. 0.75x at 96).
Now: text is its CSS size at any layout scale. Documents rendered at `PixelsPerInch` 96 (or via `HtmlDocument` with a scaled context)
get larger text and may re-wrap. The default (72) is unchanged.
