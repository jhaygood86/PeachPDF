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

Suite 12681 passed on net8.0 (`Issue1157` still fails identically on clean `main`). All four changed
lines covered, both branches 100%. Zero warnings.

The rendered proof is the `border_style` showcase's `<hr>`-vs-`<div>` section, and it got *stronger*:
per cell, the rule and its equivalent zero-height `<div>` are now **pixel-identical in MuPDF** (0.000
mean channel difference on seven of eight; `dotted` is 0.84 from sub-pixel dash phase) and 0.08–0.72 in
PDFium. Before this fix they were 1.07–2.74, because the rule's constant-2 advance made it overlap the
div slightly.

Measure that section **per cell, not per row**: each pair's offset is now the rule's own height plus
its margin, so it differs per cell (90, 90, 108, 108 raster px at 6× for the first row). A single
whole-row offset finds nothing and reports a large difference — which is what it did, and is the shape
of a measurement bug rather than a rendering one.
