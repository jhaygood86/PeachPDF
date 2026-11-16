# An image in an overflowing column flex container stops shrinking at its natural height

In a `flex-direction: column` container too short for its items, an `<img>` (or inline `<svg>`, `<iframe>`, `<video>` without a poster) used to be squeezed
to whatever the container left, down to nothing. It is now floored at its automatic minimum size (CSS Flexbox 1 §4.5), as a browser does: its natural
height, or the height its aspect ratio gives its `width` when that is a definite length and smaller. Add `min-height: 0` to let it shrink as before.
A stretched picture (no `width` of its own) floors at its natural height; a picture with a percentage `width` is unchanged.
