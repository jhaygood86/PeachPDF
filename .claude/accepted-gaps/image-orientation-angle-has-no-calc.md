# `image-orientation` rejects `calc()` angles

The grammar in `CSS/ImageOrientation.cs` is shared by the CSS-OM converter and the render layer (one parser, per the
repo convention) and accepts a plain `<angle>` dimension only. `image-orientation: calc(45deg + 45deg)` is dropped as
invalid, where the spec's `<angle>` allows `calc()`. Tracked in issue #1688.
