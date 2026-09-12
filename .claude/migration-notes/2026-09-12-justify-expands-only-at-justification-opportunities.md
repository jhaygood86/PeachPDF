# `text-align: justify` expands only at justification opportunities (issue #1013)

Three user-visible changes to how a justified line is laid out, all from the same rewrite of
`CssLayoutEngine.ApplyJustifyAlignment` and its vertical counterpart.

## 1. No more gap between two inline boxes the source has no space between

**Before:** the leftover width was divided by the line's *word count* and added after every word, so
`A<span>B</span>` on a justified line rendered with a real gap between `A` and `B` — 6.186pt at
`font: 16px monospace` in a 200pt-wide block. The same gap opened on both sides of an `<img>` or
inline `<svg>` with no source white space around it.

**Now:** expansion is distributed over
[css-text-3 §6.4.5](https://www.w3.org/TR/css-text-3/#justify-algos)'s justification opportunities
only — word separators, plus the boundary between a CJK character and whatever is next to it. `A` and
`B` render contiguous, as they do under every other `text-align` value and as a browser renders them.
A hyphen (or any other soft wrap opportunity) inside a word is not an opportunity either, so
`well-known` no longer opens up at the hyphen when the line breaks elsewhere.

## 2. The last gap on a justified line is no longer about twice the others

**Before:** dividing by the word count rather than the gap count made every gap one share too narrow,
and the unconditional "flush the last word to the end edge" step then dumped the accumulated shortfall
into the final gap. On the measurement above, eight gaps of 6.186pt and a ninth of 12.372pt.

**Now:** every gap on the line gets exactly the same expansion. (Chromium measures the same: 10.094px
at every gap on that fixture.)

## 3. An unjustifiable or overflowing line is start-aligned instead of flushed to the end edge

**Before:** a justified non-last line holding a single word — typically one long unbreakable word —
was flushed to the line's end edge, spilling past the *start* edge.

**Now:** it stays where the flow put it, at the start edge, spilling past the end edge instead. A line
with no justification opportunity is [§6.4.3](https://www.w3.org/TR/css-text-3/#justify-algos)'s
*unexpandable text*, which aligns as `text-align-last` — whose initial `auto` under `justify` is
start — and §6.1 says the same about any line whose content is too long to fit: "the contents are
start-aligned: any content that doesn't fit overflows the line box's end edge". Chromium agrees.

The same three changes apply to `writing-mode: vertical-rl`/`vertical-lr`, where they act along the
column's inline (physical Y) axis.
