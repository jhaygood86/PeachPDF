# `pointer-events` now applies to links inside inline SVG

Before, an `<a>` inside an inline `<svg>` always wrote a link annotation, whatever `pointer-events` said on it, on the `<svg>`, or on the host element. Now `pointer-events: none` (on the `<a>`, a parent, the `<svg>` or its HTML host) leaves no link, and the SVG shape keywords drop the link when no shape under the `<a>` can be hit (for example `visiblePainted` over `fill="none"` shapes).
