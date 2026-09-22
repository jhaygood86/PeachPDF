# `<hr>` margins live in the UA sheet; `align`, `size` and `color` map per element (#1235, #1249, #1234)

The last three `<hr>` deviations that belong to the element rather than to shared layout. Each was a legacy
behaviour living in the wrong layer: a substitution inside margin collapsing standing in for a missing UA rule,
and two attribute translations that were generic where the HTML Standard is per-element.

## The load-bearing idea

**A default belongs in the layer the cascade can see.** `CollapsedMarginBefore` replaced a collapsed margin
under 0.1 with `1.1em` for any `<hr>`, *after* the cascade, so nothing could override it. The fix is not a better
number: it is `hr { margin: 0.5em auto }` in `CssDefaults` and deleting the special case, which makes `margin: 0`
work by ordinary cascade, gives the rule its missing bottom margin, and centres a narrow rule for free.

## The trap that cost a round: logical vs physical

The spec spells the rule `margin-block: 0.5em; margin-inline: auto`, and that is what went in first. Eleven of
the forty-eight new tests failed on it, every one an author or `align` margin losing to the UA rule (two
more failures that run were only my own colour-string expectation). The engine's
documented limitation (html-css-support.md, logical properties) is that a logical declaration **always beats a
physical one on the same edge, whatever the origin or the order** - so a UA `margin-block` defeats every author
`margin` and every `align` hint, which is the whole point of the change. The UA sheet uses physical margins
everywhere else; `hr` now does too. Anyone tempted to "make it spec-shaped" again will hit the same wall.

## `align`, `size` and the Chrome oracle

Both mappings were written from the spec text and then measured, and two of the results overrode what the issues
said. All numbers are Chrome 153 against real `<hr>` elements:

- **`align` matches the exact value, not a trimmed one.** `align=" right "` centres (it is not `right`); `middle`,
  `foo`, `""` and valueless all centre too. The issue's wording ("left/center/right") was right but the trimming
  the generic translation does was not to be inherited.
- **A `size` that does not parse to more than 1 behaves like `size=1`** - `0`, `-1`, `abc`, empty and valueless
  all measure 0.75pt (a single border). The issue and the spec say an invalid value is "no hint". Chrome is what
  documents are authored against, so Chrome is what is reproduced, and the parse is Blink's (leading integer:
  whitespace, sign, digits, trailing text ignored: `"3px"`, `" 3"`, `"+3"`, `"3.7"` are all 3). The divergence from
  the spec is recorded in `TranslateHrSize`'s remarks.

## The `color` lead from #1248 was real

Blink maps `color`/`noshade` to `background-color` as well as the border colour. `<hr size=10 color=red>` is a
solid red bar in Chrome and was two red lines with a white gap in PeachPDF. One UA declaration fixes it:
`hr[color], hr[noshade] { background-color: currentcolor }`, which also gives a bare `noshade` its gray fill via
the sheet's own `color: gray`. It changes nothing for a rule with no content height.

## What the showcase caught that the tests did not

`border_style`'s `<hr>`-vs-`<div>` section changed on one page and nowhere else, and the reason was the fix
working: every swatch rule declares `margin: 0 0 12px`, which the old substitution had been overriding all along,
so the baseline rules were pushed down and sat visibly out of line with their `<div>` partners. Now they are flush.
The other 149 showcases are unchanged, and the new `hr_attributes` showcase agrees between PDFium and MuPDF.

## An existing test pinned the bug

`Acid2FeatureVerificationTests.Hr_WithZeroMargin_StillGetsMinimumSeparation` asserted that an `<hr>` with
`margin: 0` keeps at least 5pt of separation. It was written to document the quirk, so it failed the moment the
quirk went. Rewritten as `Hr_WithZeroMargin_HasNoSeparation`.

## Not done

`overflow: hidden` on the UA rule (the HTML Standard has it) - not needed for anything measured here.

## Evidence

Full net8.0 suite green (13,050 tests, 48 of them new), 0 warnings on a solution rebuild. Chrome measured every
literal in `HrUaMarginTests` and `HrLegacyAttributeTests` (margins, centring, `align`, `size`, `color`).
