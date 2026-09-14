# Replaced and atomic inline content sits at the line's top, not on its baseline

Tracked by [#1053](https://github.com/jhaygood86/PeachPDF/issues/1053).

CSS 2.1 [§10.8.1](https://www.w3.org/TR/CSS21/visudet.html#line-height) aligns a baseline-aligned
atomic inline by its **bottom margin edge**: an `<img>`, an inline `<svg>`, MathML, a form control or
an `inline-block` sits *on* the line's baseline, with the text beside it sharing that baseline. (For
an `inline-block` with in-flow line boxes and `overflow: visible`, the baseline is its own last
line's baseline, not its margin edge.)

PeachPDF now does this for every **non-replaced** inline box — text of mixed sizes on one line shares
one baseline, and the half-leading is honored — but leaves replaced and atomic content exactly where
the flow put it, at the line box's top edge. `CssLayoutEngine.FirstNonReplacedWordOf` is the one
place that decides this: a box whose content on the line is entirely replaced (including an inline
ancestor wrapping nothing else, so a `<span>`'s background cannot part company with the image inside
it) gets no baseline offset at all, and `LineBoxContributionOf` correspondingly leaves such content
out of the line's own extent, which is instead grown through `MaxBottom` at the call site.

Visible effect: `text <img style="height:20pt"> text` puts the image's top level with the text's line
top, where a browser puts its bottom on the text baseline.

## Why it was left

The baseline-alignment change it came out of was already large — it reached line-box sizing, word
placement, fragmentation membership and seven tests' expectations. Extending it to atomic inlines is
a separable piece of work with its own risks, and a materially different one:

- An atomic inline's own words are flowed into the **same** line coordinates as its parent's, with
  `coordinates.CurrentY` pre-shifted by its border and padding (`ApplyAtomicInlineVerticalInsets`).
  There is no separate line box to take a baseline from, so "the baseline of its last line box" has
  to be reconstructed rather than read.
- Moving replaced content changes where every image, form control, MathML run and inline `<svg>` in
  the suite and the showcases lands. That is a large, separate verification job, and a bad one to
  bundle with a change whose own correctness is argued from text positions.

Doing it half-way would be worse than not doing it: aligning text to the baseline while leaving an
image on the same line at the top would be neither the old behaviour nor the correct one.
`BaselineAlignmentLayoutIntegrationTests.ReplacedContentStaysAtTheLineTop` states the current
behaviour directly, so closing this gap is a visible, deliberate edit rather than an accident.
