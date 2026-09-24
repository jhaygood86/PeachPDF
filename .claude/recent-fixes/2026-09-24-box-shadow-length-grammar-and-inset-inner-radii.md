# box-shadow length grammar and inset inner radii (#221 items 4 and 5)

**Grammar (`BoxShadowGrammar.IsLength`).** It used to accept *any* `Dimension` token, so `2foo 2px red` parsed, and a
`calc()` in a length slot was rejected: a function is one `Function` token, `TryClassify` files anything that is not a
length under "color", and `IsValidColor` then failed the layer. Now a dimension needs a recognised length unit
(`Length.GetUnit`, excluding `%`) and a calc-family function counts as a length (same precedent as
`BasicShapeGrammar.IsLengthPercentage`; a negative calc blur cannot be rejected statically, so `ParseLength` resolves
it at paint time).

- `Length.GetUnit` was an exact lowercase switch, but CSS units are ASCII case-insensitive and `2PX` is valid. The
  first cut of `IsLength` called it directly and `2PX 2px` stopped parsing (caught by a test). Lower-casing only in the
  grammar was worse: the grammar then accepted `2PX`, but the paint-time `ParseLength` still used the exact switch and
  resolved it to 0 (found in review, not by the grammar test). The fix is at the source — `GetUnit` retries with a
  lower-cased name only on the miss path, when the name has an upper-case letter — so every caller (layout, calc,
  paint) now agrees; a paint-level `8PT` test guards it.
- `IsLength` is one production shared by `box-shadow`, `text-shadow` and the `blur()`/`drop-shadow()` filter
  functions, so all of them tightened and gained calc together. That is deliberate (CLAUDE.md: one grammar per value
  shape), not a side effect to undo.
- An unterminated `calc(1px + 2px` at end of input is *valid* CSS (an unclosed function is closed at EOF), so it is
  not a rejection case despite looking like one.

**Inset radii.** A rounded inset shadow clipped the padding box with the *border-box* radii, and the raster path's lit
hole used them too. The padding edge's radius is `border-radius` minus the border width (CSS Backgrounds 3 §5.5), floored
at zero. Both now use `ComputeInnerRadii` (the call `background-clip: padding-box` already makes), wrapped as
`PaddingEdgeRadii`; the spread adjustment is factored into `AdjustRadii` so the outset (border radii + spread) and inset
(padding radii − spread) paths share one clamp. The clip/hole gate is now `radii.IsRounded`, not `box.IsRounded`, so a
border at least as wide as the radius clips to a plain rectangle.

**Evidence.** The two clip tests and the raster pixel test were each re-run against the old painter code and fail there.
Trap: the pixel test with a full-cover shadow (spread 30) never builds the lit hole (`inner` has zero height), so it only
proves the *clip*; `BlurredInsetShadow_LitHoleFollowsThePaddingEdgeRadius` (spread 0) is the one that guards the hole,
verified by temporarily giving the hole the border radius. The pixel tests work because the arcs differ measurably on the diagonal: a radius-r corner is crossed at ~0.29·r in
(20pt radius → ~5.9pt, 30pt → ~8.8pt), so a full-cover shadow sampled at 7pt in is black only under the correct radius.
Showcase (`box_shadow`, section 6) rasterized through MuPDF and PDFium: identical shapes in both.
