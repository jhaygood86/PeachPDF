# A `visibility` keyframe on an inline `<svg>` element keeps it visible until the end

With `AnimationProgress` set, `@keyframes k { to { visibility: hidden } }` on an element of an inline `<svg>` hid the element from the first frame: the keyframe that names only `hidden` had no `visible` to start from, so `hidden` held for the whole run
(`v0.9.25-preview1`'s `SvgTreeBuilder.InitialValuesOutsideTheRegistry` has no `visibility` entry, and its `docs/supported-svg-features.md` still lists `visibility` under "Not animated"). It now follows a browser: `visibility` is discrete, so between
`visible` and `hidden` the element is visible for the whole run and hidden at the very end, and `from { hidden } to { visible }` shows it after the first instant. An element-level `visibility` that is not animated is unchanged.
