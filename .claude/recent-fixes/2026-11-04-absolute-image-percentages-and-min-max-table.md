# Percentages on an absolutely positioned image, and the min/max width limits after a height limit

**Symptom.** A card image written the way Tailwind writes it (`position: absolute; max-width: 100%; min-width: 100%; min-height: 100%; height: auto` in a
`position: relative` card) was laid out 0 wide (`max-width: 100%` alone was enough), so every such image on a page vanished. Found by writing the test for
CSS 2.1 section 10.4's constraint violation table, which the spec check had flagged, and seeing a width of 0, not the too-wide image the code reading predicted.

**Cause.** An inline element that is absolutely positioned (an `<img>` is inline) is wrapped by the parser in a synthetic absolutely positioned block and made
static itself (`DomParser.CorrectAbsolutelyPositionedInlineElements`). `CssLayoutEngine.MeasureIntrinsicSize` resolved the image's percentage `width`, `max-width`
and `min-width` against `ContainingBlock.Size.Width`, which for the image is that wrapper: a shrink-to-fit box whose width follows the image's own, so 0 while the
image is being measured (a cyclic percentage). The heights had the same problem (`IsHeightDefinite` of an auto-height wrapper is false, so `min-height: 100%`
was ignored). Separately, the width scaled by a height limit (`min-height` raising a picture, `max-height` lowering it) was never held to the width limits again,
so a picture raised to its minimum height could end up wider than its `max-width`; the table resolves both dimensions together.

**Fix.** `AbsolutePercentageBasisBox` finds the box the percentages really refer to (the nearest positioned ancestor, looking through the synthetic wrapper), and the
width and height percentage branches use `AbsoluteContainingBlockWidth`/`AbsoluteContainingBlockHeight` (the measures `left`/`right` and the auto-width fill already
use, and `IsHeightDefinite` guards the height). A percentage `max-width` whose basis has no width yet is treated as no limit (CSS Sizing 3: an unresolvable percentage
is `none` for `max-width`), not as a limit of zero. After the height limits the width is clamped to `[min-width, max-width]` (a minimum beats a maximum) when the
element has a ratio. Tests: `ReplacedElementConstraintTableTests` (min/max on each axis, the 16:9-in-4:3 card, a percentage `max-width` for absolute, relative and static).

**Also checked, no change needed.** `font-family: Source Sans\ 3` (unquoted, escaped space), unquoted identifier sequences, collapsed whitespace and case differences all select
the same `@font-face` as `"Source Sans 3"` (`FontFamilyNameSyntaxTests`): the spec check's suspicion about escaped family names was wrong; the test stays as a guard.

**Not done.** The wrapper itself is still a deviation (CSS 2.1 section 9.7 computes `display: block` on the element and makes it the absolutely positioned box, with no
wrapper); only its percentage behaviour was fixed. Replaced elements in flex/grid and the other rows of the table (a `max-height` on a picture wider than its box, both
maxima exceeded) rely on the earlier steps and the new final clamp, not on a literal implementation of every table row. Evidence: Engine 18,004, PeachBrackets 624 and
PeachPDF 1,577 tests pass; not rendered against Chrome.
