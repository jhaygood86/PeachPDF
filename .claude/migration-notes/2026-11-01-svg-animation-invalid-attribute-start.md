# An SVG animation starts from the inherited value when the attribute is invalid

Before: an element whose own `stroke-width`, `stroke-dashoffset`, `opacity`, `fill-opacity`, `stroke-opacity` or `stroke-miterlimit` presentation attribute was not a valid value (for example `stroke-width="x"`) started a CSS animation from that raw text, which the animation could not mix, so the value jumped at the first step instead of interpolating.

Now: the invalid attribute is dropped, so the animation starts from the value the property computes to (the inherited value, or the initial one for a non-inherited property) and interpolates from it.
