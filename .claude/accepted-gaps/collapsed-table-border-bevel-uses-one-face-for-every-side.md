# A collapsed table border's bevel paints one face on every side — issue #1237

**Tracked bug, not a limitation argued through and accepted.** Found while fixing #1226 and
deliberately left out of it, because it is independent of that change in both cause and reproduction.

`BorderBevelColors.ForSegment` collapses a grid line to a *direction* and then asks `ForSide` for a
face:

```csharp
ForSegment(color, isHorizontal, inset) => ForSide(color, isHorizontal ? Border.Top : Border.Left, inset);
ForSide(color, side, inset)            => Shade(color, (side is Border.Top or Border.Left) == inset);
```

Under `inset`, `Top` and `Left` both darken — so every segment darkens, whichever edge of the cell it
actually sits on. Under `outset` every segment lightens. The bevel never appears.

Measured over one cell's slot, `border-collapse: collapse` with `border: 20px inset`:

| | `#ab0000` (darkened) | `#ff0000` (lit) |
| --- | --- | --- |
| Chrome 153 | 5600 | 5600 |
| PeachPDF | 8000 | 0 |

**Not only collapsed tables.** `MarginBoxRenderer.PaintBorder` paints a page box's and a page-margin
box's border through the same `DrawCollapsedSegment` primitive, so it has the same defect: measured,
an `inset` margin-box border paints `#9a9a9a` on every edge and an `outset` one `#eeeeee` on every
edge, where Chrome paints both faces. `MarginBoxRendererBorderTests` asserts only the darkened face
for that reason, with a note to add the lit one when this closes.

**Two things that are NOT the cause**, both checked by running them, so neither is worth
re-investigating:

- **Not `currentColor`.** It reproduces identically with the colour declared
  (`border: 20px inset red`, no `color` anywhere), so #1226's resolution base is not involved.
- **Not the shading function.** `border-collapse: separate` with the same declaration matches Chrome
  exactly (5580/5580 px either way), because it goes through `BoxEdgesDrawHandler` with real sides.
  `BorderBevelColors.Shade` itself is right; only the side it is asked about is wrong.

The direction argument still has a real job — pattern fitting needs it — so the fix is to pass the
originating `Border` side *alongside* it from `BordersDrawHandler`, not to replace it. What needs its
own measurement pass is which cell a grid line shared between two cells should take its side from;
that is `CollapsedBorderModel`'s question, not the bevel helper's.

CSS 2.1 [§8.5.3](https://www.w3.org/TR/CSS21/box.html#border-style-properties) leaves bevel shading
UA-defined, so this is a fidelity gap rather than a strict spec violation — but with all four sides
identical the four bevelled keywords are indistinguishable from `solid` on a collapsed table, which
is why it is tracked as a bug.

`docs/html-css-support.md` does not describe this to readers today; if it starts to, that note and
this file are deleted together when #1237 closes.
