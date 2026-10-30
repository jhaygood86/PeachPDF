# SVG `visibility` in a keyframe needs its initial value

`@keyframes k { to { visibility: hidden } }` on an SVG element hid it from progress 0 instead of at the very end, where a browser keeps it visible for the whole run (CSS Transitions 1 section 3: between `visible` and anything else the
value is `visible`). Measured through the builder (`element.Visible`) and against Chrome's paused animations: `visible, visible, visible` at 0, 0.25 and 0.75 for Chrome, `hidden, hidden, hidden` here.

## Cause: a cleanup that was right when it was made

`SvgTreeBuilder.InitialValuesOutsideTheRegistry` is what an element that declares a property gets as the value of a keyframe's omitted 0% or 100%. It had a `visibility` entry, which a review of the SVG animation change removed because the SVG builder never read
`visibility` then (a dead entry, so the animation docs were corrected to say it was not animated). The renderer gained `visibility` afterwards, which put the property on the animation path (`ResolveStyledAttr` asks for it), with no initial value left: the
keyframe's `hidden` was the only value known, so it held for the whole run.

## Fix and rule

The entry is back (`visible`). `visibility` needs no entry in `InheritedSvgProperties`: it is inherited in `css-properties.json`, so `CssDefaults.InheritedProperties` already supplies it and an animated `<g>` reaches its children. A property the builder starts reading through `ResolveStyledAttr` has to be given its initial value and inheritance in
`SvgTreeBuilder` (the registry knows neither for a property that is not in `css-properties.json`'s SVG bindings), or its keyframes lose the omitted end: grep `InitialValuesOutsideTheRegistry` when a property is added.

## Evidence

`SvgCssAnimationTests`: `to { hidden }` is visible at 0, 0.25 and 0.75 and hidden at 1; `from { hidden } to { visible }` is hidden at 0 and visible after; an animated `<g>` hides its child. Without the entry exactly the three `to { hidden }` cases at 0, 0.25 and 0.75 fail; the other cases pin the discrete rule (two fail when it is disabled) and the inheritance down a group. Chrome agrees at all nine samples
checked (the two keyframe shapes and an attribute `visibility="hidden"` under `to { visible }`, at 0, 0.25 and 0.75). The documented limitation (a bare number and a comma-separated dash list in a keyframe are not read) was already closed by the CSS-OM
fix; two cases pin that it works inside a keyframe.
