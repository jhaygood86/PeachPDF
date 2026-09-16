# An automatic underline hangs from the line's own baseline, not from its content's top edge

`PaintDecoration` placed an automatic underline at
`rectangle.Top + font.TextBaselineOffset + gap + thickness / 2`. Two of those terms come from
different places: `rectangle.Top` is *content* geometry (the union of the inline content on the line,
or the box's own per-line rectangle), while `TextBaselineOffset` is measured in the *decorating box's*
font.

The sum is the baseline only while every glyph on the line is set in that same font. A line carrying
any other size is underlined one ascent-difference away, in whichever direction the mismatch runs —
above the glyphs for a small decorating block, well below the line for a large one. Issue #1111 has
the numbers; the worst fixture put the line 14.8pt from where it belonged, on 12px text.

## The load-bearing idea

The line knows its own baseline, and every word on it agrees about where that is. Layout places each
word's rectangle exactly its own (rounded) `RFont.Ascent` above the baseline, so
`wordRect.Y + wordStyle.ActualFont.Ascent` is the same number for every baseline-aligned word however
large it is set. `DecorationContent.AlphabeticBaselineOn` returns the first one it finds and the rest
cannot disagree — no averaging, no tie-break.

`PaintDecoration` then applies **the decorating box's own** ascent rounding
(`- (font.Ascent - font.TextBaselineOffset)`) to reach the baseline the glyphs are painted on, which is
also what makes the whole expression collapse back to `rectangle.Top + TextBaselineOffset` for a
single-font line. Every such document paints byte-for-byte as it did before.

## What the spec actually says, having fetched it

Less than the first draft of this claimed, and in a different section. The position rule is
css-text-decor-3 **§2.5** (§2.4 is the `text-decoration` shorthand, and the propagation rule this file
set is often cited for is in §2's own introductory prose, before §2.1 — several existing comments in
this repo cite §2.4 for it):

- "The exact position and thickness of line decorations is **UA-defined in this level**. However, for
  underlines and overlines the UA **must use a single thickness and position on each line** for the
  decorations deriving from a single decorating box." The second half is the part this fix satisfies and
  the old expression did not — a position derived from each rectangle's own top is not one position per
  line.
- Its note is the reasoning: "since line decorations can span elements with varying font sizes and
  vertical alignments, **the best position for a line decoration is not necessarily the ideal position
  dictated by the decorating box**." So consulting the line's real content is what the spec's own
  reasoning favours, and "measure from the decorating box's font" — closer to what the old code did — is
  not a rule it states.
- The `vertical-align` exclusion, by contrast, *is* normative and is the strongest citation here: a UA
  "must adjust line positions to match the shifted metrics of decorating boxes shifted with
  `vertical-align` values other than `baseline` ... but **must not** adjust the line position or
  thickness in response to **descendants** of a decorating box that are so styled." Both halves fall out
  of where `IsShiftedOffTheBaseline` stops walking.

## Found by running it, not by reading it

**`CssLineBox.BaselineY` is the obvious anchor and is wrong.** It was the first implementation. It
agrees with the painted baseline everywhere except inside a table cell, where the UA stylesheet's
`vertical-align: middle` moves the content block *after* the line closed: a `line-height: 2` cell
reported 17.601 against a painted baseline of 20.014. Taking it would have silently moved every
underline in every table cell by the centering offset — a regression into exactly the shape of markup
that makes the most use of underlines. Only a probe printing both numbers side by side caught it,
because the suite has no fixture that pins an underline inside a centred cell.

That is also the architecturally correct outcome: paint's contract is the fragment tree, and the word
rectangles in it are what `DrawString` is handed. `CssLineBox` is layout's own record and is allowed
to disagree.

**A superscript has to be excluded, and the box that carries the shift is not the one you first
reach.** The word's owner is an anonymous box whose `vertical-align` is the initial `baseline`; the
`super` lives on the `<sup>` above it. So the check walks the owner chain. It stops *below* the
decorating box, which matters for the same table cell: `td { vertical-align: middle }` positions the
cell's content block, it does not tilt any line inside the cell, and treating it as a shift would have
discarded every word in the cell and fallen back to the broken expression.

## Deliberately not done

- **`line-through` and `overline` still key off the rectangle** (`rectangle.Top + Height / 2`,
  `rectangle.Top`). Both are wrong for the same reason on a mixed-size line, and a `line-through` also
  sits a little below the x-height centre browsers use on a *single*-size line — on the order of 5% of
  the glyph-ink height against Chrome, so a calibration rather than a mismatch. Either way that is its
  own change with its own before/after, not a rider on this one.
- **`text-decoration-thickness: auto` stays at the engine's fixed 1**, so a heading's underline is
  still a hairline where Chrome scales the stroke with the font. That is a recorded, deliberate choice
  (see `docs/html-css-support.md`), not a consequence of this change.

## Evidence

- `UnderlineBaselineIntegrationTests`, 12 cases. Reverting `Html/Core/Paint/` to `main` fails 7 of
  them and leaves 5 passing — the 4 being the single-font control and its neighbours, which is the
  bracket: a test that passes either way is not measuring the fix.
- Full suite on net8.0: 11,864 passed, 1 failed —
  `AnonymousBoxDefaultingTests.AListItemCostsLittleMoreThanAPlainBlock`, which fails identically on
  clean `main` (2.62x there, 2.61x here, against a 2.55x bound). Pre-existing, and not touched by a
  decoration-only change.
- Against Chrome 141 (`--headless --print-to-pdf`, rasterized at 300dpi, underline band measured
  relative to the glyph ink): the mixed-size fixture that diverged by **+53 device pixels** now
  diverges by 0. The single-font fixtures are unmoved, which is the point.

## Cost

`DecorationContent` now always collects words, where it used to skip them for a box that had opted out
of `text-decoration-skip-ink`. The walk itself is unchanged and only ever runs for a box that declares
a decoration, so this adds a dictionary insert per word of decorated text and nothing at all to an
undecorated document. `collectWords` was the only caller of `WantsInkFrom`, so both are gone.
