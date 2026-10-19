# A percentage opacity in a keyframe mixes, and `fill-opacity` / `stroke-opacity` accept one

`opacity: 50%` in a `@keyframes` step used to flip against a number (`opacity: 1` to `opacity: 50%` showed 1 until the midpoint, then 0.5) for an HTML box;
it now mixes as the number it stands for (0.75 halfway, as in a browser; `git show v0.9.23:src/PeachPDF/Html/Core/Animation/CssValueInterpolator.cs` has no such step). `fill-opacity: 50%` and
`stroke-opacity: 50%` in a stylesheet rule or keyframe used to be dropped as invalid (`style=""` and the attribute already read them) (their grammar was a number only, `v0.9.23:src/PeachPDF/CSS/StyleProperties/Fill/FillOpacityProperty.cs`); they are read
now, as `opacity` and `stop-opacity` always were.
