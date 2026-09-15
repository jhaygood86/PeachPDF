# A thick underline grows away from the baseline

`FragmentPainter.PaintDecoration` used the font's one fixed `UnderlineOffset` as the center of every
underline stroke. That was plausible for a hairline, but a 6px stroke grew three pixels upward from
the same center and crossed ordinary glyph ink. Since `text-decoration-skip-ink: auto` correctly
measures the line's whole band, it then exposed the positioning error as a row of gaps.

The underline center is now derived from the alphabetic baseline, a browser-compatible automatic gap
of `max(1px, ceil(thickness / 2))`, and half the stroke width. The last term matters because PeachPDF's
adapter primitive draws a centered stroke, whereas browser decoration geometry is represented as a
top-anchored rectangle.

The regression test asserts both the exact center and the more important invariant: the stroke's top
edge remains at least one CSS pixel below the baseline.
