# Text continuing onto a page with a `float: top` no longer runs under the float

**Before:** a paragraph that started on one page and carried on onto a page that reserves room for a
`float: top` at its head drew its first continued lines in that reserved strip, underneath the float.

**Now:** the continued lines start below the strip, as a block-level box already did. A document with no
`float: top` is unaffected.
