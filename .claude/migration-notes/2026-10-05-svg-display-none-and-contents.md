# `display` now applies to SVG elements

Before: PeachPDF's SVG renderer never read `display`. An SVG element with `display: none` (an attribute, `style=`, or a rule) still painted, with all of its children, and `display: contents` on a group was an ordinary group, with its own transform, opacity, clip, mask and filter.

After: `display: none` removes the element and its subtree, wherever it is reached from (a `<use>` instance, a pattern or marker, a `<tspan>`). `display: contents` on `g`, `a`, `switch`, `use`, `tspan` and `textPath` strips the element and keeps its content, without the element's own box effects; on any other SVG element it computes to `none`. Documents that set `display: none` on an element and relied on it still drawing (for example a hidden copy referenced by `<use>`) now render nothing for it.
