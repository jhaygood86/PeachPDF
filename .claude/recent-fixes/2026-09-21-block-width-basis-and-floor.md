# Auto-margin centring basis and the border-box width floor (#1244, #1246)

Found while fixing #1230 (`<hr>` percentage width) and split out because a plain `<div>` reproduces both, so
neither belongs in `CssBoxHr`.

## The load-bearing idea

`ContainingBlock.Size.Width` is a **content** width under `content-box` and a **border box** under
`border-box`. `ContainingBlock.AvailableWidth` is the content width under either. Every read that means
"the containing block's content width" wants the second; the first is right only for the container that
almost every document uses, which is why this survived. Same lesson as
[2026-09-21-hr-resolves-a-percentage-width-against-its-containing-block.md](2026-09-21-hr-resolves-a-percentage-width-against-its-containing-block.md),
in a different function.

## #1244

`ResolveAutoHorizontalMargin`'s single `containingWidth` local feeds the auto-fill, both min/max clamps and the
definite-width `remaining`, so the one substitution covers all of them. The audit turned up the same read in the
percentage-margin basis (`GetActualMarginLeft/Right`, `CssBox.ActualMarginTop/Bottom`) and the table `boxWidth`
centring branch; they moved too. `AvailableWidth == Size.Width` under `content-box`, so only `border-box`
containers change.

Deliberately **not** changed: the remaining `ContainingBlock.Size.Width` reads in the intrinsic-size code
(`CssBox.cs` min-width percentage in the shrink walk, `CssBoxImage`, `CssBoxMath`, `FragmentEmitter`'s
min/max-width clamps, `CssLayoutEngine` word sizing). They are percentage bases for other properties with
their own callers and their own tests, and widening this change to them would have made it unreviewable.

## #1246

The floor is the **last** statement in `GetBoxWidth`, after both `max-width` and `min-width`, so a `max-width`
cannot squeeze the box's edges out either. It is one expression for both `box-sizing` values, the floor on the
content box spelled in whichever box `width` names: `border-box` floors at padding + border, `content-box` at
0 - which is a fix in its own right, since the auto branch goes negative there in a container with no width to
give. The issue only named `border-box`; the `content-box` case fell out of the same failing test.

## What was found by running it

- The first version of the tests paired an `<hr>` with the `<div>` and the `<hr>` half failed for a reason
  unrelated to the fix: `CssBoxHr` resolves its own width and never reaches `GetBoxWidth`. The floor tests
  assert the `<div>` alone; the rule's parity is asserted when it goes through the shared path.
- The `content-box` "contrast" case had to declare a border. An `<hr>` carries the UA sheet's 1px border, a
  `<div>` does not, and the two widths differ by exactly that.

## Not done

`GetBoxHeight` has the mirror question (a `border-box` height smaller than the box's own edges). Whether it
should floor is separate and is recorded in
[2026-09-20-hr-resolves-its-own-used-height.md](2026-09-20-hr-resolves-its-own-used-height.md).
