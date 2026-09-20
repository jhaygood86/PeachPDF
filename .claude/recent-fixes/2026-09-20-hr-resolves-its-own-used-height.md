# `<hr>` resolves its own used height (issues #1229, #1232)

A rule's used height, the position of the next in-flow sibling, and the height of a container holding
only the rule were all the same constant: **2 units**, whatever the rule's borders or declared
`height`. One expression caused all three.

```csharp
double height = ActualHeight;
if (height < 1)
    height = Size.Height + ActualBorderTopWidth + ActualBorderBottomWidth;
if (height < 1)
    height = 2;
```

`PlaceAsBlockChild`, a few lines above, writes `ActualBottom = Location.Y`, and that setter stores
`Size.Height = value - ActualBoxSizeIncludedHeight - Location.Y`. So by the time this runs
`Size.Height` is exactly **`-(borderTop + borderBottom)`** — instrumented: `-1.5` with the UA default's
0.75pt borders, `-3` with 2px ones, and `ActualHeight` `0` in both. Adding the borders back to that
cancels to zero every time. **The middle branch was unreachable**, and every rule fell through to the
hard-coded `2`.

## The two symptoms are the same line

**A painted gap inside the rule (#1232).** A 2pt border box with less than 2pt of border leaves the
remainder as real *content*, which paints as white between the two border edges. The UA default rule
drew three bands at high zoom — `#9a9a9a`, white, `#eeeeee` — where a browser draws two. It is
`max(0, 2pt - borders)`, so a *thinner* author border is worse than the default: `border: 0.5pt` got a
1pt gap, wider than either border it separated.

**A constant flow advance and container height (#1229).** The same 2 is what the frame commits the
next sibling's offset against, and what a container sized by its content takes. A `border: 10px` rule
was overlapped by whatever followed it and overflowed its own parent by 13pt.

## The fix, and the second half that is not obvious

```csharp
double height = CssLayoutEngine.GetBoxHeight(this) ?? 0;
if (height <= 0)
    height = ActualBorderTopWidth + ActualBorderBottomWidth;
if (height <= 0)
    height = 2;
```

Dropping the poisoned `Size.Height` term fixes the auto-height case. It does **not** fix a rule with a
declared `height`, and that took tracing the call order to see:

```
COMMIT child=hr#el top=26
HR-LAYOUT ActualHeight=0 sizeH=-1.5 declaredHeight='10px'
  HR-DONE height=1.5 bottom=27.5          <- the declared 10px never reached it
COMMIT child=div#nx top=27.5              <- the sibling is committed HERE
  APPLYHEIGHT before bottom=27.5 boxHeight=9
  APPLYHEIGHT before bottom=35            <- corrected, one step too late
```

A declared height normally arrives via `CssLayoutEngine.ApplyHeight` in `PerformLayoutEpilogue`, which
for this box runs *after* the frame has already placed the next sibling. Reading `ActualHeight` could
never see it, because `ActualHeight` goes through the poisoned `Size.Height`. Calling `GetBoxHeight` —
`ApplyHeight`'s own resolver, and the only one that does not read `Size.Height` back — resolves the
declared height inside the rule's own pass, which is what puts the real bottom in place before the
sibling is committed against it.

## Padding, and why a floor is not the fix

`GetBoxHeight` returns a *border-box* height — it adds `ActualBoxSizeIncludedHeight` itself — and the
old `ActualBottom = Location.Y + ActualPaddingTop + ActualPaddingBottom + height` then added the
padding a second time. So `ActualBottom` is now `Location.Y + height`, full stop.

The obvious companion change is to **floor** the resolved height at the box's own edges
(`borders + padding`) rather than only substituting them when nothing resolved. It gives the same
answer on every case but one, and that one is the reason not to do it: `box-sizing: border-box;
height: 5px; padding: 5px` has a border box smaller than its own edges. `GetBoxHeight` returns it as
declared, and `ApplyHeight` re-runs `GetBoxHeight` in the epilogue — so a floor applied only here puts
the *flow* at the floored value while the *paint* stays at the declared one. Whether `GetBoxHeight`
should clamp that case is its own question; what this method must not do is answer it differently from
the paint. Hence a fallback (`height <= 0`), not a floor.

## `max-height` had to come along

`GetBoxHeight` does not consider `min`/`max-height` at all — `ApplyHeight` applies them itself,
afterwards, in the epilogue. That is one pass too late for the same reason the declared height was:
the sibling is already committed. `<hr style="height: 20px; max-height: 5px">` painted 5.25 and placed
the next block at +2.

Rather than re-deriving the clamp, `ApplyHeight`'s trailing block is extracted as
`CssLayoutEngine.ClampToMaxHeight` and called from both places — the repo's "one resolver, two callers"
rule. It only ever *shrinks* a bottom past the maximum, so running it twice is a no-op the second time.

`<= 0` rather than the old `< 1` deliberately: with `ActualHeight` always 0 the old threshold was
dead code, but once real values flow through it, `< 1` would reject a genuinely small declared height
(`height: 0.5pt`) and a thin border pair. Nothing relies on the 1.

## What still reaches the constant, and why it stays

A rule with neither a height nor a border. Removing the `2` there is tempting — Chrome gives that rule
height 0 — but a zero-size box **emits no fragment at all**, so it would be absent from the fragment
tree rather than merely invisible, and `BorderlessHr_PaintsNothing` fails on the missing fragment
rather than on anything about painting. Tried, measured, reverted.

That leaves one inconsistency, pre-existing and unchanged: a borderless rule's box is 2 while the flow
advance it produces is 0, because it collapses through. Invisible, since nothing paints either way.

## Evidence

Suite 12706 passed on net8.0, zero failures — the `Issue1157` failure that was outstanding while this
was written has since been fixed on `main` by #1228. Zero warnings.

`Hr_UsedHeight_IsItsContentPlusItsOwnBorders` and `NextInFlowSibling_StartsAtTheRulesBottom` carry the
padding, `box-sizing: border-box` and `max-height` cases. Both `<= 0` cases are pinned on the
**sibling offset**, not only on the used height: the epilogue re-resolves the rule's own bottom
afterwards, so a wrong value there is corrected before anything reads it back, and only the sibling's
committed offset catches the difference. Reverting `<= 0` to `< 1` fails three assertions; before that
was noticed it failed only one.

The rendered proof is the `border_style` showcase's `<hr>`-vs-`<div>` section, and it got *stronger*:
per cell, the rule and its equivalent zero-height `<div>` are **pixel-identical in MuPDF** on seven of
eight cells (0.000 mean channel difference; `dotted` is 0.837 from sub-pixel dash phase) and 0.08–0.72
in PDFium. Before this fix they were 1.07–2.74, because the rule's constant-2 advance made it overlap
the div slightly.

Measuring that section is worth three warnings, because each of them produced a confident wrong
number on the way to the ones above:

1. **Per cell, not per row.** Each pair's offset is now the rule's own height plus its margin, so it
   differs per cell (90, 90, 108, 108 raster px at 6× for the first row). A single whole-row offset
   finds nothing and reports a large difference.
2. **Locate the swatches by shape, not by position.** Cell heights differ now, so row boundaries taken
   from one cell do not apply to the next.
3. **Start the scan window below the section heading.** Both the `<h2>` text and its `border-bottom`
   span the full page width, so "spans this cell horizontally" matches them too — and the underline
   survives a minimum-height filter on the wider cells while the heading text survives it on all of
   them. Cells 3 and 4 looked fine throughout, because the heading text does not reach that far right,
   which is exactly the kind of partial agreement that reads as a real rendering difference.
