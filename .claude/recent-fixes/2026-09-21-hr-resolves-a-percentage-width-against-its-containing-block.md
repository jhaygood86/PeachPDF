# `<hr>` resolves a percentage width against its containing block, not against itself (#1230)

`CssBoxHr.PerformLayoutImp` computed one `width` expression — the available space, already reduced by
the rule's own margins and borders — and used it for **both** the `auto` case and the percentage
basis. CSS 2.1 [§10.2](https://www.w3.org/TR/CSS21/visudet.html#the-width-property) resolves a
percentage against the containing block's *content* width, with the element's own margins, borders and
padding outside it. The two are now separate statements.

## The load-bearing idea

The `auto` expression is correct where it belongs and wrong as a basis, and that is the entire bug:
one value doing two jobs. Splitting it is a five-line change; the work was in establishing which
subtrahends belong to which half.

- **Percentage basis**: containing block content width. Nothing of the rule's own comes out of it.
- **`auto`**: that same width, less the rule's own margins, borders **and padding** — CSS 2.1 §10.3.3's
  constraint solved for `width`, which is what makes an unstyled rule's margin box span its container
  exactly.

## Not only a styled-rule problem

Worth knowing because it changes how widely this was visible: the UA sheet gives every rule a 1px
border, which is 0.75pt a side. So a plain `<hr style="width: 50%">` in a 200pt block came out
**99.25pt** rather than 100 — wrong on a document that never declares a border at all. With a declared
`border: 4px` it was 97pt, and with a margin as well the margin came out of the basis too
(`margin-left: 20pt` → 87pt).

## Found by running it, not by reading it

- **The first raster probe said the bug did not exist.** An `<hr>` and its paired zero-height `<div>`
  were drawn 0px apart, so the two painted bands merged into one and the measurement returned the
  union — i.e. the div's (correct, wider) span, with the rule's narrower one hidden inside it. The
  layout-level assertions found it immediately. If a pixel probe of a paired hr/div ever says
  "no difference", separate the two boxes before believing it.
- **`box-sizing: border-box` was fixed by the same change**, unmeasured beforehand and not mentioned
  in the issue: `ParseLength` applies box-sizing on top of whatever basis it is handed, so a wrong
  basis was compounded there. `width: 50%; box-sizing: border-box` painted 96px of a 200px block
  before and 100px after, which is Chrome's answer.
- **`auto` with horizontal padding overflowed its container** by exactly that padding (measured: 220px
  inside a 200px block, against Chrome's and the equivalent div's 200), because only margins and
  borders were taken out of the available space. Fixed alongside — it is the same statement, one line
  down, and leaving it would have kept "a rule and its equivalent div agree" false.
- **`Size.Width` is not always a content width.** Under `box-sizing: border-box` it holds the border
  box. A test asserting content-box arithmetic there fails with values that look like a layout bug and
  are not one.

## What this unblocks

Pairing a rule against its equivalent zero-height `<div>` — the natural way to test or showcase rule
painting — is now valid *at any width*. It previously held only with no `width` declared, which is why
the `border_style` showcase's `<hr>` section had to avoid declaring one, and why an earlier version of
it shipped four mismatched pairs. The showcase gained two rows that do declare widths.

## Evidence

- Full suite green (12,718 passed / net8.0), whole solution rebuilds with 0 warnings, 100% diff
  coverage.
- 12 tests in `HrPercentageWidthLayoutTests.cs`, each asserting the rule **and** its equivalent div
  against the same literal, since "these two agree" is the property that actually matters.
- A 7-case probe (percentages, a length, `auto`, a margin, `box-sizing: border-box`) rendered through
  Chrome headless and PeachPDF via PDFium at 96 dpi: every case pixel-identical after the fix, and the
  `auto`-with-padding case separately.
- A standalone 100/75/50/25 % deck agrees with Chrome to within one pixel at 50% and 25%, where the
  edge lands on a half-pixel (127.5px, 63.75px) and the two rasterizers round opposite ways. The
  paired `<div>` shows the identical 1px delta, which is how that is known not to be an `<hr>` issue.
