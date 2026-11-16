# The declarative builder gains the container methods a CSS property already backs

Added to `IContainer` and friends, each a one-line setter over a property the engine already supports (full list in `docs/declarative-api.md`): `MinWidth`/`MaxWidth`/`MinHeight`/`MaxHeight`, `Opacity`, `ZIndex`, `Direction`/`ContentFromLeftToRight`/`ContentFromRightToLeft` (also on `IPageDescriptor`), `Rotate`/`FlipHorizontal`/`FlipVertical`/`FlipOver`/`TranslateX`/`TranslateY`/`TransformScale`/`TransformScaleX`/`TransformScaleY`, `PreventPageBreak`/`ShowEntire`, `PageBreak`, `MultiColumn` and `Columns`/`ColumnGap`/`ColumnRule`/`ColumnFill`, the named font weights, `WrapAnywhere`, `Decoration*` and `Align*`/`Justify` on the paragraph.

**What was measured, not assumed.** The names follow another library's, so its behavior was run (its 2026.9.2 package, a throwaway console project, rasterized and bounding boxes read from the pixels) before deciding what maps onto CSS:
- Its `Rotate`, `Flip*` and `Translate*`/`Offset*` move the painting and keep the layout, like CSS `transform`; `Rotate` turns around the center. Those names are used as they are.
- Its `Scale`, `ScaleHorizontal`, `ScaleVertical` and `RotateLeft`/`RotateRight` scale or swap the **layout** (neighbors move). CSS `transform` cannot do that and there is no `zoom`, so they are not offered; the paint-only versions have their own names (`TransformScale*`, `Rotate(90)`).
- `PreventPageBreak` is `break-inside: avoid` (moves to the next page when it fits, splits when it does not). `ShowEntire` throws when the content is taller than a page; here it is an alias that still splits.
- `EnsureSpace` (a minimum of room on the page), `ScaleToFit`, `Unconstrained`, `Placeholder` and the multi-column `Spacer` element have no CSS route and are not offered.

**Traps found by rendering.** See [the invariant](../invariants/declarative-builder-boxes-are-inline-until-a-method-makes-them-blocks.md): the transforms painted nothing until the box was made a block, a break-after on a block nested in a flex item is not carried to the next item (so `PageBreak` sets it on the container itself), and a flex column does not continue into the next column (so `MultiColumn` content stacks blocks).

**Evidence.** `DeclarativeApiNamedElementsTests` (27 cases: resolved properties, precedence over a stylesheet, laid-out sizes, page and column positions, a `HasTransform` check); the `declarative_transforms_and_flow` showcase rasterized with PDFium and MuPDF, which agree. Engine suite 20118 and PeachPDF.Tests 2085 pass on net10.0.
