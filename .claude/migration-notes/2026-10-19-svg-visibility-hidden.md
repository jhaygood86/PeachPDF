# SVG `visibility: hidden` now hides the element

Before: `visibility` was ignored on SVG content, so `<rect visibility="hidden">` (attribute, `style=` or a rule) was drawn. Now `hidden` and `collapse` stop the element painting, the value is inherited, and a descendant with `visibility: visible` is painted. A hidden element still counts for bounding boxes, clip paths and text layout.
