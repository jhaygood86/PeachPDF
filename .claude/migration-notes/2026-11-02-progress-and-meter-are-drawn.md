# `<progress>` and `<meter>` are drawn

**Before:** a `<progress>` or `<meter>` had no default size and drew nothing; an inline element with no content took no room, and its fallback text (`<progress>70%</progress>`) was drawn as ordinary text.

**Now:** they are inline-blocks ten ems wide and one em tall (`vertical-align: -0.2em`) drawn as a rounded track with a fill to their value - a progress bar in its `accent-color` (a blue when it has none), a meter in green, yellow or red by the region its value is in - and their content is no longer drawn. This applies to every output, a PDF included.

**Why:** the HTML standard's rendering section gives them this look, and an accessible document that says "70% complete" with nothing on the page to show it was wrong. A document that relied on fallback text to show the number, or that did not expect an element to take room, will change; set `display: none` on the element to get the old result, or style it (`width`, `height`) to taste.
