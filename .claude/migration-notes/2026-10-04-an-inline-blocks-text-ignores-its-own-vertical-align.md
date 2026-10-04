# An inline-block's text ignores its own vertical-align

Before: a padded inline-block with `vertical-align: top`, `middle`, `text-top` or `text-bottom` drew its text
over its top padding, at the border edge.
After: its text starts below the padding, as in Chrome. The value still places the inline-block in its line.
