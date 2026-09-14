# Line, word, and nested-fragment geometry can diverge across font metrics

Tracked by [#1048](https://github.com/jhaygood86/PeachPDF/issues/1048).

Line boxes correctly use the CSS 2.1 §10.8/§10.8.1 used `line-height`, including negative leading,
but several adjacent systems do not yet share one ownership contract with that geometry:

- vertical-writing float exclusion samples a column edge instead of its prospective full block-axis
  span;
- word rectangles retain the resolved font's natural height and can cross the line box that owns
  their fragmentation;
- a table continued inside a balanced multi-column fragment can place its first resumed line above
  the captured column band, leaving those words emitted nowhere.

Fonts whose natural metrics differ materially from their normal line height expose these paths more
readily, which is why Linux/macOS fallback fonts found failures that Windows Arial did not. Tests in
the Acid2 branch use bundled fonts and explicit geometry so host font availability does not decide
their pagination or assertions.

The attempted combined repair crossed inline layout, table continuation, multi-column capture,
fragment membership, and paint geometry, and regressed unrelated pagination cases. It was reverted
to keep the Acid2 work scoped. Closing this gap requires a focused implementation that preserves
negative leading while making line ownership authoritative across all four layers, with dedicated
cross-platform tests using deliberately different bundled font metrics.
