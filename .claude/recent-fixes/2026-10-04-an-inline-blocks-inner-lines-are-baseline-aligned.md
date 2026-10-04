# An inline-block's inner lines are baseline-aligned

_CSS 2.1 §10.8.1. Tracker: #1439._

`CssLayoutEngine.EffectiveVerticalAlignOf` walked from a text box up to the nearest element and read its
`vertical-align`. For an inline-block's own lines that element is the inline-block, whose value says how it sits
in its parent's line. With top padding, `top`/`text-top`/`text-bottom`/`middle` put the text over the padding
(y=20 against 50 with 30pt padding). A table cell was already excluded the same way; an inline-block or
inline-table that owns the line is now too, and reads `BaselineVerticalAlign`.

- **Why it waited.** The change moves lines off the page boundaries they happened to sit on, and a tall
  inline-block is laid out whole and sliced, so boundary lines were lost (53 and 54 of 59 words). That was the
  slice-boundary loss fixed in [the slice claim](2026-10-04-a-line-straddling-a-slice-boundary-is-drawn-by-both-pages.md);
  with it in place all 59 words show and the first word's top is 50.25 against Chrome's 50.2.
- **Evidence.** `VerticalAlignIntegrationTests` (two of six cases fail without the change) and
  `SlicedMonolithLinesTests.ATallPaddedTopAlignedInlineBlock_ShowsEveryWord`; the CLI render of the issue's
  document through pymupdf.
- The accepted-gap file for this is deleted; the ink-based `ClaimsLine` rescue stays for other cases.
