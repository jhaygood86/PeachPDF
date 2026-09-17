# Wrapped inline outlines have open break edges

Tracked in [#1163](https://github.com/jhaygood86/PeachPDF/issues/1163).

When an inline element wraps across line boxes, PeachPDF currently applies
`box-decoration-break: slice`-style geometry to its outline: every fragment retains its physical
top/bottom edges, the first fragment has the physical start edge, the last fragment has the physical
end edge, and the inline-axis edges at internal line breaks remain open.

[CSS Basic User Interface Level 4](https://drafts.csswg.org/css-ui-4/#outline) does not prescribe
the exact position or shape of a fragmented outline, but recommends that each part be fully
connected rather than open on some sides. Chromium closes every line fragment independently,
while Firefox constructs a different connected outline; PeachPDF's open break edges therefore
miss that connectedness recommendation even though neither browser shape is uniquely required.
`box-decoration-break` does not settle the difference because its slicing rules do not govern
outlines.

Closing or joining the fragments needs an outline-specific fragment-union or minimum-outline
algorithm and a deliberate interoperability choice. The shared edge painter deliberately keeps
physical start/end-edge semantics neutral for borders and outlines, so this is not safely fixed
by making it close every partial edge set.
