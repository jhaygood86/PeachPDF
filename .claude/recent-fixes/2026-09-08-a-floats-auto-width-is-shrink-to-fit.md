# A float's auto width is shrink-to-fit

`GetBoxWidth` gave a floating box with `width: auto` the stretch-to-containing-block width. CSS 2.1
§10.3.5 makes it shrink-to-fit. A float that fills its containing block leaves `float: right`
nowhere to go, so it lands at the left as a full-width bar and the content that belongs beside it is
pushed onto its own line.

## What running it turned up

- **Float PLACEMENT was never wrong, which is what made this hard to see.** A float with a DECLARED
  width lands on Chrome's x to the point, so the placement machinery looks correct under any test
  that declares a width. Only the auto measurement was wrong, and it is the auto case that a badge
  or a pull-quote actually uses. There is a fixture pinning the declared-width case precisely so the
  change is visibly scoped to `auto`.
- **The formula is already in the tree.** `min(max-content, max(min-content, available))` is what
  `GetFitContentWidth`/`GetMinContentWidth` compute between them, and the orthogonal-flow shrink in
  `CssBox.cs` already uses that pair — so this is the existing §10.3.5 implementation reused rather
  than a second one.
- **Both measurements are OUTER widths**, so they compare against the stretch width as it stands and
  the box's own decoration comes back off at the end — the same subtraction the plain auto branch
  makes, and a no-op under `box-sizing: border-box`.
- **A 1px border insets the float's own text by 0.75pt**, so "the text sits beside the float" cannot
  be asserted as an equal baseline. The first version of that fixture compared tops to ±0.05pt and
  failed against correct behaviour.

## Evidence

`FloatAutoWidthTests`, four fixtures: an auto `float: right` reaching the right of a 300pt container
(Chrome: 257.9pt in), text sitting beside an auto `float: left` on the same line, a declared width
unchanged at exactly 45pt, and an auto float never exceeding its containing block. The first two
fail against `main`. Full suite green on net8.0 (10,256), 0 new build warnings.
