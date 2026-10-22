# `background-image: inherit`

Cause: the cascade resolves `inherit` by reading the parent's property as a string (`CssUtils.GetPropertyValue`) and feeding it back through the setter. `background-image` is stored as parsed layers (`CssBox.BackgroundImages`, `IReadOnlyList<CssImage?>`) and its entry in `css-properties.json` has `hasGetter: false`, so the read returned null and the declaration was skipped (`if (value is null) continue;`). Every other background longhand is a string with a getter, which is why the colour, position, repeat and size were inherited and only the image was not.

Fix: `DomParser` special-cases `background-image: inherit` and copies the parent's layers instead of round-tripping text. A `CssImage.Url` owns a loaded `ImageLoadHandler` that `CssBox.Dispose` disposes, so sharing the parent's `Url` would dispose it twice (and share a handler across boxes); `CssImage.CopyForInheritance()` returns a fresh, unloaded `Url(Href)` (loaded later with the box's other auxiliary images) and shares the immutable gradient records. `background-image` is not an inherited property, so only the explicit keyword takes this path; `unset` still means `initial`.

Not done: a string getter for `background-image` (would need the original text or a serializer for every gradient form), which would also let `revert` restore it.

Evidence: `BackgroundInheritIntegrationTests` counts the image draws (parent alone 1, with a child that inherits 2) for the shorthand and the longhand forms; both fail without the change.
