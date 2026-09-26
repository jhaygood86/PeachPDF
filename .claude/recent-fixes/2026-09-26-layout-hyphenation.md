# Hyphenation in `PeachDrawing.Text.Layout`

`ParagraphStyle.Hyphens` (`Manual` default, `None`, `Auto`), `HyphenateCharacter`, `HyphenateLimitChars`, `HyphenateLimitLines`, `HyphenateLimitZone`,
`HyphenateLimitLast`; `LineEnd.Hyphenated`; `PlacedRun.IsGenerated`.

- **A hyphen is a generated run, not text.** A line broken at a soft hyphen or an automatic point gets a `PlacedRun` with an empty `Range` at the break, in the paragraph's
  direction (appended for LTR, inserted first for RTL), counted in `LineBox.Width`. Its one caret boundary is the edge the text meets it at, so a caret at the break is
  before the hyphen in either direction. `IsSoftEnd` in `ParagraphLayout` names the line ends a position can be on either side of (`Soft`, `Emergency`, `Hyphenated`); the
  three copies of that test were inline `is Soft or Emergency` and would have missed the new kind.
- **The soft hyphen's own glyph is dropped at shaping time**, and that was found by running it: Source Sans 3 maps U+00AD, and the shaper keeps a mapped glyph (only an
  unmapped default ignorable is hidden), so an unbroken "hy&shy;phen" was 6 units wider and drew a hyphen mid-word. `WithoutSoftHyphens` removes the glyph from the shaped
  piece (remapping `AttachedToIndex`), the characters stay in the text and the caret code already gives an uncovered offset the boundary before it.
- **`FitLine` records break candidates** (position, line width, whether it needs a hyphen) so that a break after a soft hyphen whose hyphen does not fit backs up to the last
  earlier place that has room, instead of overflowing. With no place that has room the hyphen overflows (never loops). `HyphenateLimitLines` needs the count of hyphenated lines
  before this one, which is why `FitLine` takes `hyphenRun`: the per-line function stays pure and the host-driven tier's cursor will carry that count.
- **Order of remedies for a word that does not fit:** hyphenate it (`Auto`, as far along as fits with the hyphen), else break before it, and for the first word of a line hyphenate,
  else `OverflowWrap` cut, else overflow. Limit-zone applies only when there is something before the word on the line; limit-last needs the next forced break, found by scanning the
  opportunities.
- **Pattern points are cached per (start, end)** in the paragraph, and the hyphen glyph run per (style, string), both bounded, since a resize lays out at many widths.
- `Hyphens.None` also turns the soft hyphen's opportunity off in the paragraph constructor (UAX #14 allows a break after it).

Not done: `hyphenate-limit-last` values other than `always` (a paragraph has no columns or pages); Auto in WebAssembly (the pattern resources are Brotli).
Evidence: `ParagraphHyphenationTests` (27), all other layout tests unchanged.

A review measured two quadratic paths that are fixed: `Hyphenator` was run on the whole remainder of a huge word for every line (a run of letters over 128 UTF-16 units is not a word and is not hyphenated; the rest of a word is hyphenated as part of the whole word, cached once, so the limits count from the word's start), and `HyphenateLimitLast` measured the whole rest of the hard line for every candidate (`FitsOnALineOfItsOwn` now stops as soon as the words are wider than the next line, whose room `FitLine` takes as `nextRoom` because a first-line indent made the old test stricter than reality). Break candidates are only tracked when the text has a soft hyphen. The pattern engine's own minimums (2 before, 3 after for English) are a floor under `HyphenateLimitChars`; the doc says so.
