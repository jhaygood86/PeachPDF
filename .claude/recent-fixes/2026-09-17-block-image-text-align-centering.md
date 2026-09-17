# A `display: block` image/SVG is no longer centered by an ancestor's `text-align`

A `display: block` `<img>`/`<svg>` inside a `text-align: center` (or `right`) ancestor rendered centered
(or flush right), even with no `margin-left`/`margin-right: auto` of its own.

## The load-bearing idea

This engine can only size a replaced element as a single atomic inline "word", so
`DomParser.CorrectReplacedElementBoxes` wraps a `display: block` `<img>`/`<svg>` in a synthetic anonymous
block (`CssBox.IsReplacedBlockWrapper = true`) and forces the element's own `Display` back to `inline`.
That wrapper is never a real inline formatting context an author can see — the same fact
`LineBoxContributionOf` already relies on to skip the CSS 2.1 §10.8 strut there (issue #1127) — but
`CssLayoutEngine.ApplyHorizontalAlignment`/`ApplyVerticalTextAlignment` were never given the matching
exemption: they read the wrapper's own inherited `ActualTextAlignAll` like any real line and dispatched
to `ApplyCenterAlignment`/`ApplyRightAlignment` accordingly.

[css-text-3 §6.1](https://www.w3.org/TR/css-text-3/#text-align-property) scopes `text-align` (and, by
the same reasoning, `text-align-last`) to a block's inline-level content only — never to where a
block-level box itself sits. Both functions now return/flush-to-start immediately for a wrapper's line,
before ever reading `ActualTextAlignAll`/`ActualTextAlignLast`. Horizontal's natural (pre-alignment)
placement is already flush-start (a bare early return suffices); vertical's is not (see
`ApplyVerticalTextAlignment`'s own remarks), so its guard explicitly flushes to the column's own
inline-start edge instead.

The wrapped element's own position is a margin question, not a text-align one
([CSS 2.1 §10.3.3](https://www.w3.org/TR/CSS21/visudet.html#blockwidth), deferred to for a replaced
element by [§10.3.4](https://www.w3.org/TR/CSS21/visudet.html#block-replaced-width)) — but
`GetActualMarginLeft`/`GetActualMarginRight`'s auto-margin centering (`ResolveAutoHorizontalMargin`) read
`box.ActualBoxSizingWidth`, which is always `0` for this wrapped box since it never goes through
`GetBoxWidth`'s own block-width resolution. So an explicit `margin-left: auto; margin-right: auto` — the
spec-correct way to opt back into centering — computed a wrong offset too. `ResolveAutoHorizontalMargin`
now reads the element's own declared `width` directly (clamped by `max-width`/`min-width`, mirroring the
auto-width branch's own clamp a few lines below it) for this case, rather than the unpopulated
`Size.Width`.

## Deliberately not done

An element relying on its *intrinsic* (no declared `width`) size still computes a wrong `margin: auto`
centering offset — `box.FirstWord.Width` isn't populated yet at the point this same question is resolved
during flow. See
`.claude/accepted-gaps/replaced-block-wrapper-intrinsic-width-margin-auto-centering.md` (issue #1178).

## Evidence

`ReplacedBlockWrapperAlignmentIntegrationTests.cs` (new): a block image not centered/flushed by an
ancestor `text-align: center`/`right`; still centers via explicit `margin: auto`, including with
`max-width`/`min-width` clamping; an ordinary (non-block) inline image still centers normally
(non-regression); the vertical-writing-mode counterpart under both LTR and RTL. Full net8.0 suite:
12,296 passed, 0 failed, 9 platform skips. Diff coverage: 100%. Full solution rebuild: 0 warnings.
